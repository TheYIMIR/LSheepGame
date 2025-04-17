using Mirror;
using UnityEngine;
using UnityEngine.UI;

public class NetworkLobbyManager : NetworkBehaviour
{
    [Header("Lobby Settings")]
    [SyncVar(hook = nameof(OnCountdownChanged))]
    public float currentCountdown = 60f;

    [SyncVar(hook = nameof(OnCountdownActiveChanged))]
    public bool countdownActive = false;

    [SyncVar(hook = nameof(OnVotesChanged))]
    public int votesToStart = 0;

    [SyncVar(hook = nameof(OnGameStartedChanged))]
    public bool gameStarted = false;

    [Header("UI References")]
    public Text countdownText;
    public Text votesText;
    public Text playersCountText;

    // Reference to the LobbyUI for client-side updates
    private LobbyUI lobbyUI;

    // Reference to the SheepNetworkManager
    private SheepNetworkManager networkManager;

    // Called when the script instance is being loaded
    public override void OnStartServer()
    {
        base.OnStartServer();

        // Find the network manager
        networkManager = FindObjectOfType<SheepNetworkManager>();

        // Reset lobby state
        ResetLobbyState();

        // Start countdown automatically
        StartCountdown();

        // Update UI initially
        UpdateServerInfo();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // Find the lobby UI
        lobbyUI = FindObjectOfType<LobbyUI>();

        // Find the network manager
        if (networkManager == null)
        {
            networkManager = FindObjectOfType<SheepNetworkManager>();
        }

        if (lobbyUI != null)
        {
            // Update UI with current state
            UpdateUIFromSyncVars();
        }
    }

    void Update()
    {
        if (isServer && countdownActive && !gameStarted)
        {
            // Update countdown on server
            currentCountdown -= Time.deltaTime;

            if (currentCountdown <= 0)
            {
                StartGame();
            }

            // Update server info periodically
            if (Time.frameCount % 60 == 0) // About once per second at 60fps
            {
                UpdateServerInfo();
            }
        }

        // Update UI on client
        if (!isServer && lobbyUI != null && countdownActive)
        {
            lobbyUI.UpdateCountdownUI(currentCountdown);
        }
    }

    // Initialize UI from sync vars when client starts
    void UpdateUIFromSyncVars()
    {
        if (lobbyUI != null)
        {
            // Update countdown
            lobbyUI.UpdateCountdownUI(currentCountdown);

            // Update votes if we have player counts
            if (networkManager != null)
            {
                int playerCount = networkManager.CountConnectedPlayers();
                lobbyUI.UpdateVotesUI(votesToStart, playerCount);
                lobbyUI.UpdatePlayersUI(playerCount);
            }
        }
    }

    // Updates the server info for UI
    void UpdateServerInfo()
    {
        if (!isServer) return;

        int playerCount = 0;

        // Use the improved counting method from network manager
        if (networkManager != null)
        {
            playerCount = networkManager.CountConnectedPlayers();
            RpcUpdatePlayerCount(playerCount);

            // Update votes UI
            if (playerCount > 0)
            {
                RpcUpdateVoteCount(votesToStart, playerCount);
            }
        }
    }

    // Update player count info for all clients
    public void UpdatePlayerCountUI(int playerCount)
    {
        if (isServer)
        {
            RpcUpdatePlayerCount(playerCount);
        }
    }

    // Server method to register a vote
    public void PlayerVoted()
    {
        if (!isServer) return;

        votesToStart++;

        // Check if enough votes to skip
        CheckVoteThreshold();
    }

    // Start the countdown
    public void StartCountdown()
    {
        if (!isServer) return;

        countdownActive = true;
        currentCountdown = NetworkGameConfig.LOBBY_COUNTDOWN_SECONDS;
    }

    // Start the game
    public void StartGame()
    {
        if (!isServer || gameStarted) return;

        gameStarted = true;

        Debug.Log("Starting game from lobby manager!");

        // If we have a network manager, tell it to change scenes
        if (networkManager != null)
        {
            // Fill empty slots with bots
            networkManager.FillWithBots();

            // Change to the game scene
            networkManager.ServerChangeScene(NetworkGameConfig.GAME_SCENE_NAME);
        }
    }

    // Reset lobby state
    public void ResetLobbyState()
    {
        if (!isServer) return;

        gameStarted = false;
        votesToStart = 0;
        countdownActive = false;
        currentCountdown = NetworkGameConfig.LOBBY_COUNTDOWN_SECONDS;
    }

    // Check if enough votes to skip countdown
    void CheckVoteThreshold()
    {
        if (!isServer) return;

        int playerCount = 0;

        // Use the improved counting method from network manager
        if (networkManager != null)
        {
            playerCount = networkManager.CountConnectedPlayers();

            // Debug log the votes
            Debug.Log($"Votes: {votesToStart}/{playerCount}");
        }
        else
        {
            playerCount = NetworkServer.connections.Count;
        }

        if (playerCount < 1) return;

        float votePercentage = (float)votesToStart / playerCount;

        // If at least 50% voted to start
        if (votePercentage >= NetworkGameConfig.VOTE_SKIP_PERCENTAGE)
        {
            // Set countdown to 3 seconds
            currentCountdown = NetworkGameConfig.VOTE_SKIP_COUNTDOWN_SECONDS;

            // Notify clients
            RpcVoteThresholdReached();
        }

        // Update the UI with current vote count
        RpcUpdateVoteCount(votesToStart, playerCount);
    }

    // Called when countdown changes
    void OnCountdownChanged(float oldValue, float newValue)
    {
        // Update UI on clients
        if (!isServer && lobbyUI != null)
        {
            lobbyUI.UpdateCountdownUI(newValue);
        }
    }

    // Called when countdown active state changes
    void OnCountdownActiveChanged(bool oldValue, bool newValue)
    {
        // Enable/disable UI elements based on countdown state
        if (!isServer && lobbyUI != null)
        {
            // Could enable/disable countdown text here
            if (newValue)
            {
                lobbyUI.countdownText.gameObject.SetActive(true);
            }
        }
    }

    // Called when vote count changes
    void OnVotesChanged(int oldValue, int newValue)
    {
        // Update is handled through CheckVoteThreshold and RpcUpdateVoteCount
        Debug.Log($"Votes changed from {oldValue} to {newValue}");

        // Update all clients with new vote count
        if (isServer && networkManager != null)
        {
            int playerCount = networkManager.CountConnectedPlayers();
            RpcUpdateVoteCount(newValue, playerCount);
        }
    }

    // Called when game started changes
    void OnGameStartedChanged(bool oldValue, bool newValue)
    {
        // React to game started state change (could show "Game Starting..." text)
        if (newValue && !oldValue && !isServer && lobbyUI != null)
        {
            // Show game starting message
            lobbyUI.countdownText.text = "Game Starting...";
        }
    }

    // Client RPC to update vote count
    [ClientRpc]
    void RpcUpdateVoteCount(int votes, int playerCount)
    {
        if (playerCount <= 0) return;

        if (lobbyUI != null)
        {
            lobbyUI.UpdateVotesUI(votes, playerCount);
        }
        else if (votesText != null)
        {
            float percentage = (float)votes / playerCount * 100f;
            votesText.text = $"Votes: {votes}/{playerCount} ({Mathf.FloorToInt(percentage)}%)";
        }
    }

    // Client RPC to update player count
    [ClientRpc]
    void RpcUpdatePlayerCount(int playerCount)
    {
        if (lobbyUI != null)
        {
            lobbyUI.UpdatePlayersUI(playerCount);
        }
        else if (playersCountText != null)
        {
            int maxPlayers = NetworkGameConfig.MAX_PLAYERS_PER_LOBBY;
            playersCountText.text = $"Players: {playerCount}/{maxPlayers}";
        }

        // Also update votes display with new player count
        if (isServer)
        {
            RpcUpdateVoteCount(votesToStart, playerCount);
        }

        Debug.Log($"Updated player count to: {playerCount}");
    }

    // Notify clients that vote threshold was reached
    [ClientRpc]
    void RpcVoteThresholdReached()
    {
        Debug.Log("Vote threshold reached! Game starting soon...");

        // Update UI with skipped countdown
        if (lobbyUI != null)
        {
            lobbyUI.countdownText.text = "Starting Soon!";
        }
    }
}