using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

// This class should be attached to the GameManager object
[RequireComponent(typeof(GameManager))]
public class NetworkGameManager : NetworkBehaviour
{
    // Reference to the base game manager
    private GameManager gameManager;

    // Network properties
    [SyncVar(hook = nameof(OnGameStartedChanged))]
    private bool networkGameStarted = false;

    [SyncVar(hook = nameof(OnRemainingCountChanged))]
    private int remainingSheepCount = 0;

    [SyncVar(hook = nameof(OnGameOverChanged))]
    private bool networkGameOver = false;

    // This tracks if sheep includes both AI and players or just players
    [SyncVar]
    public bool lastManStandingPlayerOnly = false;

    // Timer to periodically sync state
    private float syncStateTimer = 0f;
    private float syncInterval = 0.5f;

    private bool allSheepInitialized = false;
    private float gameStartDelay = 3f;

    // Track the winner for proper UI display
    private GameObject winnerSheep = null;

    private void Awake()
    {
        // Get reference to the game manager
        gameManager = GetComponent<GameManager>();

        if (gameManager == null)
        {
            Debug.LogError("NetworkGameManager requires a GameManager component!");
            enabled = false;
            return;
        }

        // Mark the game manager as network enabled
        gameManager.isNetworkGame = true;
    }

    // When server starts
    public override void OnStartServer()
    {
        base.OnStartServer();

        Debug.Log("NetworkGameManager starting on server");

        // Initialize game mode from PlayerPrefs
        if (PlayerPrefs.HasKey("PlayersOnlyMode"))
        {
            lastManStandingPlayerOnly = PlayerPrefs.GetInt("PlayersOnlyMode") == 1;
            Debug.Log($"Network game using Players Only Mode: {lastManStandingPlayerOnly}");
        }
        else
        {
            // Default to false - both AI and players count
            lastManStandingPlayerOnly = false;
        }

        // Register for death events
        GameEvents.onSheepDied += OnSheepDied;

        // Start the delay coroutine to prevent premature win conditions
        StartCoroutine(DelayGameStart());

        // Monitor game manager state
        StartCoroutine(MonitorGameManager());
    }

    public IEnumerator DelayGameStart()
    {
        allSheepInitialized = false;
        Debug.Log("Delaying game start checks to allow all sheep to initialize...");
        
        // Wait for the initialization period
        yield return new WaitForSeconds(gameStartDelay);
        
        // Now it's safe to check win conditions
        allSheepInitialized = true;
        Debug.Log("Game initialization complete - now checking win conditions");
    }

    void OnDestroy()
    {
        // Unregister from events
        GameEvents.onSheepDied -= OnSheepDied;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // Initialize client-side state
        if (!isServer)
        {
            Debug.Log("NetworkGameManager started on client");

            // Request current state from server
            StartCoroutine(RequestInitialState());
        }
    }

    private IEnumerator RequestInitialState()
    {
        // Wait for connection to be established
        yield return new WaitForSeconds(0.5f);

        // Request current game state if we're not the server
        if (!isServer)
        {
            CmdRequestGameState();
        }
    }

    [Command(requiresAuthority = false)]
    private void CmdRequestGameState()
    {
        // Send current state to the requesting client
        if (gameManager != null)
        {
            TargetReceiveGameState(
                NetworkClient.connection,
                networkGameStarted,
                networkGameOver,
                remainingSheepCount,
                lastManStandingPlayerOnly
            );
        }
    }

    [TargetRpc]
    private void TargetReceiveGameState(NetworkConnection target, bool gameStarted, bool gameOver, int sheepCount, bool playerOnlyLastManStanding)
    {
        Debug.Log($"Received initial game state. Started: {gameStarted}, Over: {gameOver}, Sheep: {sheepCount}, PlayersOnly: {playerOnlyLastManStanding}");

        if (gameManager != null)
        {
            gameManager.gameStarted = gameStarted;
            gameManager.gameOver = gameOver;

            // Update UI
            if (gameManager.sheepRemainingText != null)
            {
                gameManager.sheepRemainingText.text = $"Sheep Remaining: {sheepCount}";
            }

            gameManager.playersOnlyMode = playerOnlyLastManStanding;

            // Unlock all sheep if game has started
            if (gameStarted)
            {
                gameManager.EnableAllSheep();
            }
        }
    }

    // Event handler for sheep deaths
    void OnSheepDied(GameObject sheep)
    {
        if (isServer)
        {
            Debug.Log($"NetworkGameManager received sheep death event: {sheep.name}");
            CheckGameEndConditions();
        }
    }

    // Coroutine to monitor game state changes on server
    private IEnumerator MonitorGameManager()
    {
        yield return new WaitForSeconds(1f); // Wait for setup

        int lastCount = 0;
        bool lastGameOver = gameManager.gameOver;

        while (true)
        {
            // First, refresh the active sheep list
            if (isServer && gameManager.activeSheep != null)
            {
                // Remove any null references first
                gameManager.activeSheep.RemoveAll(sheep => sheep == null);

                // Get current count
                int currentCount = gameManager.activeSheep.Count;

                // Check if count changed
                if (currentCount != lastCount)
                {
                    lastCount = currentCount;
                    remainingSheepCount = currentCount;

                    Debug.Log($"Sheep count updated to: {remainingSheepCount}");

                    // Force update UI on all clients
                    RpcUpdateSheepCount(remainingSheepCount);

                    // Check for game end conditions
                    CheckGameEndConditions();
                }
            }

            // Check if game state has changed
            if (gameManager.gameStarted && !networkGameStarted)
            {
                networkGameStarted = true;

                // Make sure all movement is unlocked
                RpcUnlockAllSheepMovement();
            }

            // Check if game over state has changed
            if (gameManager.gameOver != lastGameOver)
            {
                lastGameOver = gameManager.gameOver;
                networkGameOver = gameManager.gameOver;
            }

            // Periodically sync state to all clients
            syncStateTimer += Time.deltaTime;
            if (syncStateTimer >= syncInterval)
            {
                syncStateTimer = 0f;
                SyncActiveSheepList();
            }

            yield return new WaitForSeconds(0.2f); // Check every 0.2 seconds
        }
    }

    // Check game end conditions specifically for network play
    private void CheckGameEndConditions()
    {
        if (!allSheepInitialized || !gameManager.gameStarted)
        {
            return;
        }

        if (!isServer || gameManager.gameOver) return;

        // Count human players that are still alive
        List<NetworkSheepPlayer> humanPlayersAlive = new List<NetworkSheepPlayer>();

        // Separate AI and player counts
        int aiSheepAlive = 0;

        foreach (GameObject sheep in gameManager.activeSheep)
        {
            if (sheep == null) continue;

            NetworkSheepPlayer netPlayer = sheep.GetComponent<NetworkSheepPlayer>();
            if (netPlayer != null && !netPlayer.isDead)
            {
                humanPlayersAlive.Add(netPlayer);
            }
            else if (sheep.GetComponent<NetworkAISheep>() != null)
            {
                aiSheepAlive++;
            }
        }

        // Debug log the current state
        Debug.Log($"Game state: {humanPlayersAlive.Count} human players, {aiSheepAlive} AI sheep, PlayersOnly mode: {lastManStandingPlayerOnly}");

        // Only check for last man standing when there's just one human player left
        if (humanPlayersAlive.Count == 1)
        {
            NetworkSheepPlayer lastPlayer = humanPlayersAlive[0];

            if (lastManStandingPlayerOnly)
            {
                // In Players Only mode, if only one human player remains, they win
                Debug.Log($"Player {lastPlayer.playerName} wins (Player Only Mode)!");
                winnerSheep = lastPlayer.gameObject;
                RpcAnnounceWinner(lastPlayer.netId);
                gameManager.gameOver = true;
            }
            else
            {
                // In Standard mode, check if this is the last sheep overall
                if (aiSheepAlive == 0)
                {
                    // Last human player with no AI left
                    Debug.Log($"Player {lastPlayer.playerName} wins!");
                    winnerSheep = lastPlayer.gameObject;
                    RpcAnnounceWinner(lastPlayer.netId);
                    gameManager.gameOver = true;
                }
            }
        }
        else if (humanPlayersAlive.Count == 0 && aiSheepAlive > 0)
        {
            // All human players are dead, only AI remain - game over
            Debug.Log("All players eliminated! AI wins!");
            RpcAnnounceAIWinner();
            gameManager.gameOver = true;
        }
        else if (humanPlayersAlive.Count == 0 && aiSheepAlive == 0)
        {
            // No sheep left at all (shouldn't happen, but just in case)
            Debug.Log("No sheep remaining! Game over with no winner!");
            RpcAnnounceAIWinner();
            gameManager.gameOver = true;
        }

        // Update network game over state
        if (gameManager.gameOver)
        {
            networkGameOver = true;
        }
    }

    // Sync the active sheep list between server and clients
    private void SyncActiveSheepList()
    {
        if (!isServer) return;

        // Remove any null references
        gameManager.activeSheep.RemoveAll(sheep => sheep == null);

        // Update the count
        int currentCount = gameManager.activeSheep.Count;
        if (currentCount != remainingSheepCount)
        {
            remainingSheepCount = currentCount;
            // Force update UI on all clients
            RpcUpdateSheepCount(remainingSheepCount);
        }
    }

    [ClientRpc]
    public void RpcUpdateSheepCount(int newCount)
    {
        // Update UI on clients
        if (gameManager != null && gameManager.sheepRemainingText != null)
        {
            gameManager.sheepRemainingText.text = $"Sheep Remaining: {newCount}";
        }
    }

    [ClientRpc]
    private void RpcAnnounceWinner(uint winnerNetId)
    {
        if (isServer) return; // Server already handled this

        // Find the winner by netId
        if (NetworkClient.spawned.TryGetValue(winnerNetId, out NetworkIdentity winnerIdentity))
        {
            NetworkSheepPlayer winner = winnerIdentity.GetComponent<NetworkSheepPlayer>();
            if (winner != null)
            {
                // Check if this is the local player
                bool isLocalPlayerWinner = winner.isLocalPlayer;

                // Show appropriate UI
                if (isLocalPlayerWinner)
                {
                    Debug.Log("You won!");
                    if (gameManager.victoryPanel != null)
                    {
                        gameManager.victoryPanel.SetActive(true);
                    }

                    // Play victory sound
                    gameManager.PlayRandomSound(gameManager.victorySounds);
                }
                else
                {
                    Debug.Log("You lost! Another player won.");
                    if (gameManager.defeatPanel != null)
                    {
                        gameManager.defeatPanel.SetActive(true);
                    }

                    // Play defeat sound
                    gameManager.PlayRandomSound(gameManager.defeatSounds);
                }

                // Show buttons
                ShowEndGameButtons();
            }
        }
    }

    [ClientRpc]
    private void RpcAnnounceAIWinner()
    {
        if (isServer) return; // Server already handled this

        Debug.Log("You lost! AI sheep are the only ones left.");

        // All players lost, show defeat
        if (gameManager.defeatPanel != null)
        {
            gameManager.defeatPanel.SetActive(true);
        }

        // Play defeat sound
        gameManager.PlayRandomSound(gameManager.defeatSounds);

        // Show buttons
        ShowEndGameButtons();
    }

    // Helper to show end game buttons
    private void ShowEndGameButtons()
    {
        // Show restart button
        if (gameManager.restartButton != null)
        {
            gameManager.restartButton.gameObject.SetActive(true);
        }

        // Show main menu button
        if (gameManager.mainMenuButton != null)
        {
            gameManager.mainMenuButton.gameObject.SetActive(true);
        }
    }

    // Called when the game started state changes
    void OnGameStartedChanged(bool oldValue, bool newValue)
    {
        if (newValue && !oldValue)
        {
            // Game started notification for clients
            Debug.Log("Network game has started!");

            // Update local game state
            if (gameManager != null)
            {
                gameManager.gameStarted = true;
            }

            // Unlock all sheep movement on clients
            if (!isServer)
            {
                gameManager.EnableAllSheep();
            }
        }
    }

    // Called when the game over state changes
    void OnGameOverChanged(bool oldValue, bool newValue)
    {
        if (newValue && !oldValue && !isServer)
        {
            Debug.Log("Network game is over!");

            if (gameManager != null)
            {
                gameManager.gameOver = true;
            }
        }
    }

    // Called when the remaining sheep count changes
    void OnRemainingCountChanged(int oldValue, int newValue)
    {
        if (!isServer && gameManager != null)
        {
            Debug.Log($"Sheep count changed from {oldValue} to {newValue}");

            // Update UI on clients
            if (gameManager.sheepRemainingText != null)
            {
                gameManager.sheepRemainingText.text = $"Sheep Remaining: {newValue}";
            }
        }
    }

    // Unlock movement for all sheep - server version that broadcasts to clients
    [ClientRpc]
    void RpcUnlockAllSheepMovement()
    {
        gameManager.EnableAllSheep();
    }
}