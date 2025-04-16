using Mirror;
using UnityEngine;
using UnityEngine.UI;

public class NetworkLobbyManager : NetworkBehaviour
{
    [Header("Lobby Settings")]
    [SyncVar(hook = nameof(OnCountdownChanged))]
    public float currentCountdown = 60f;

    [SyncVar]
    public bool countdownActive = false;

    [SyncVar(hook = nameof(OnVotesChanged))]
    public int votesToStart = 0;

    [SyncVar]
    public bool gameStarted = false;

    [Header("UI References")]
    public Text countdownText;
    public Text votesText;
    public Text playersCountText;

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
    }

    // Updates the server info for UI
    void UpdateServerInfo()
    {
        if (!isServer) return;

        int playerCount = 0;

        // Use the improved counting method from network manager
        if (networkManager != null)
        {
            playerCount = networkManager.CountTotalPlayers();
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
        currentCountdown = 60f; // Initial countdown of 60 seconds
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
            networkManager.ServerChangeScene("SheepBattleground");
        }
    }

    // Reset lobby state
    public void ResetLobbyState()
    {
        if (!isServer) return;

        gameStarted = false;
        votesToStart = 0;
        countdownActive = false;
    }

    // Check if enough votes to skip countdown
    void CheckVoteThreshold()
    {
        if (!isServer) return;

        int playerCount = 0;

        // Use the improved counting method from network manager
        if (networkManager != null)
        {
            playerCount = networkManager.CountTotalPlayers();
        }
        else
        {
            playerCount = NetworkServer.connections.Count;
            // Add host if in host mode
            if (NetworkServer.active && NetworkClient.active)
            {
                playerCount++; // Count the host
            }
        }

        if (playerCount < 1) return;

        float votePercentage = (float)votesToStart / playerCount;

        // If at least 50% voted to start
        if (votePercentage >= 0.5f)
        {
            // Set countdown to 3 seconds
            currentCountdown = 3f;

            // Notify clients
            RpcVoteThresholdReached();
        }

        // Update the UI with current vote count
        RpcUpdateVoteCount(votesToStart, playerCount);
    }

    // Called when countdown changes
    void OnCountdownChanged(float oldValue, float newValue)
    {
        UpdateCountdownUI();
    }

    // Called when vote count changes
    void OnVotesChanged(int oldValue, int newValue)
    {
        // Update is handled through CheckVoteThreshold and RpcUpdateVoteCount
    }

    // Update the countdown UI
    void UpdateCountdownUI()
    {
        if (countdownText != null)
        {
            countdownText.text = $"Game starts in: {Mathf.CeilToInt(currentCountdown)}";
        }
    }

    // Client RPC to update vote count
    [ClientRpc]
    void RpcUpdateVoteCount(int votes, int playerCount)
    {
        if (votesText != null && playerCount > 0)
        {
            float percentage = (float)votes / playerCount * 100f;
            votesText.text = $"Votes: {votes}/{playerCount} ({Mathf.FloorToInt(percentage)}%)";
        }
    }

    // Client RPC to update player count
    [ClientRpc]
    void RpcUpdatePlayerCount(int playerCount)
    {
        if (playersCountText != null)
        {
            int maxPlayers = 100;
            if (networkManager != null)
            {
                maxPlayers = networkManager.maxPlayersPerLobby;
            }
            playersCountText.text = $"Players: {playerCount}/{maxPlayers}";
        }
    }

    // Notify clients that vote threshold was reached
    [ClientRpc]
    void RpcVoteThresholdReached()
    {
        Debug.Log("Vote threshold reached! Game starting soon...");
    }
}