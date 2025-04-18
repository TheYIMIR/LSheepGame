using Mirror;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages the lobby/room phase of the network game using Mirror's NetworkRoomManager framework.
/// Handles player readiness, countdown, and transitioning to the game scene.
/// </summary>
public class NetworkLobbyManager : NetworkBehaviour
{
    [Header("Lobby Settings")]
    [SyncVar(hook = nameof(OnCountdownChanged))]
    public float currentCountdown = 60f;

    [SyncVar(hook = nameof(OnCountdownActiveChanged))]
    public bool countdownActive = false;

    [SyncVar(hook = nameof(OnGameStartedChanged))]
    public bool gameStarted = false;

    [Header("UI References")]
    public Text countdownText;
    public Text votesText;
    public Text playersCountText;
    public Button backButton;

    // Reference to the SheepNetworkManager 
    private SheepNetworkManager networkManager;
    private LobbyUI lobbyUI;

    // Called when the script instance is being loaded
    public override void OnStartServer()
    {
        base.OnStartServer();

        // Find the network manager
        networkManager = NetworkManager.singleton as SheepNetworkManager;

        // Reset lobby state
        ResetLobbyState();

        // Start countdown automatically if auto-start is enabled
        if (networkManager != null && networkManager.autoStartCountdown)
        {
            StartCountdown();
        }

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
            networkManager = NetworkManager.singleton as SheepNetworkManager;
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

        // Check if game has started and update UI accordingly
        if (gameStarted && backButton != null)
        {
            backButton.GetComponentInChildren<Text>().text = "Disconnect";
        }
    }

    // Initialize UI from sync vars when client starts
    void UpdateUIFromSyncVars()
    {
        if (lobbyUI != null)
        {
            // Update countdown
            lobbyUI.UpdateCountdownUI(currentCountdown);

            // Update votes/readiness if we have player counts
            if (networkManager != null)
            {
                int playerCount = networkManager.CountConnectedPlayers();
                int readyCount = CountReadyPlayers();
                lobbyUI.UpdateVotesUI(readyCount, playerCount);
                lobbyUI.UpdatePlayersUI(playerCount);
            }
        }
    }

    // Count how many players are ready
    int CountReadyPlayers()
    {
        int readyCount = 0;

        // Find all network room players and count ready ones
        NetworkRoomPlayer[] roomPlayers = FindObjectsOfType<NetworkRoomPlayer>();
        foreach (NetworkRoomPlayer player in roomPlayers)
        {
            if (player.readyToBegin)
            {
                readyCount++;
            }
        }

        return readyCount;
    }

    // Updates the server info for UI
    void UpdateServerInfo()
    {
        if (!isServer) return;

        int playerCount = 0;
        int readyCount = 0;

        // Use the counting method from network manager
        if (networkManager != null)
        {
            playerCount = networkManager.CountConnectedPlayers();
            readyCount = CountReadyPlayers();

            RpcUpdatePlayerCount(playerCount);

            // Update votes/ready UI
            if (playerCount > 0)
            {
                RpcUpdateVoteCount(readyCount, playerCount);
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
            // Set the bot count in the network manager if needed
            if (networkManager.botSettingsFromLobby)
            {
                networkManager.ConfigureBotSettings();
            }

            // Change to the game scene
            networkManager.ServerChangeScene(NetworkGameConfig.GAME_SCENE_NAME);
        }
    }

    // Reset lobby state
    public void ResetLobbyState()
    {
        if (!isServer) return;

        gameStarted = false;
        countdownActive = false;
        currentCountdown = NetworkGameConfig.LOBBY_COUNTDOWN_SECONDS;
    }

    // Check if enough players are ready to start the game
    public void CheckReadyToStart()
    {
        if (!isServer) return;

        int playerCount = 0;
        int readyCount = 0;

        // Use the improved counting method from network manager
        if (networkManager != null)
        {
            playerCount = networkManager.CountConnectedPlayers();
            readyCount = CountReadyPlayers();

            // Debug log the ready count
            Debug.Log($"Ready Players: {readyCount}/{playerCount}");
        }
        else
        {
            playerCount = NetworkServer.connections.Count;
        }

        if (playerCount < 1) return;

        float readyPercentage = (float)readyCount / playerCount;

        // If at least 50% are ready
        if (readyPercentage >= NetworkGameConfig.VOTE_SKIP_PERCENTAGE)
        {
            // Set countdown to 3 seconds
            currentCountdown = NetworkGameConfig.VOTE_SKIP_COUNTDOWN_SECONDS;

            // Notify clients
            RpcVoteThresholdReached();
        }

        // Update the UI with current ready count
        RpcUpdateVoteCount(readyCount, playerCount);
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

    // Called when game started changes
    void OnGameStartedChanged(bool oldValue, bool newValue)
    {
        // React to game started state change
        if (newValue && !oldValue)
        {
            // Game has started, update UI
            if (lobbyUI != null)
            {
                lobbyUI.countdownText.text = "Game Starting...";

                // Update back button text
                if (lobbyUI.backButton != null)
                {
                    lobbyUI.backButton.GetComponentInChildren<Text>().text = "Disconnect";
                }
            }

            // Change the back button text
            if (backButton != null)
            {
                backButton.GetComponentInChildren<Text>().text = "Disconnect";
            }
        }
    }

    // Client RPC to update vote count
    [ClientRpc]
    void RpcUpdateVoteCount(int readyCount, int playerCount)
    {
        if (playerCount <= 0) return;

        if (lobbyUI != null)
        {
            lobbyUI.UpdateVotesUI(readyCount, playerCount);
        }
        else if (votesText != null)
        {
            float percentage = (float)readyCount / playerCount * 100f;
            votesText.text = $"Ready: {readyCount}/{playerCount} ({Mathf.FloorToInt(percentage)}%)";
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
            int readyCount = CountReadyPlayers();
            RpcUpdateVoteCount(readyCount, playerCount);
        }

        Debug.Log($"Updated player count to: {playerCount}");
    }

    // Notify clients that vote threshold was reached
    [ClientRpc]
    void RpcVoteThresholdReached()
    {
        Debug.Log("Ready threshold reached! Game starting soon...");

        // Update UI with skipped countdown
        if (lobbyUI != null)
        {
            lobbyUI.countdownText.text = "Starting Soon!";
        }
    }
}