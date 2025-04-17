using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using UnityEngine.SceneManagement;

// Network Manager for Sheep Last-Man-Standing Game
public class SheepNetworkManager : NetworkManager
{
    public static new SheepNetworkManager singleton { get; private set; }

    [Header("Player Settings")]
    public string playerName = "Sheep";

    [Header("Prefabs")]
    public GameObject lobbyManagerPrefab;
    public GameObject networkGameManagerPrefab;

    [Header("Server Settings")]
    [Tooltip("Maximum players that can join a lobby")]
    public int maxPlayersPerLobby = 100;
    [Tooltip("True to use dedicated server mode")]
    public bool isDedicatedServer = false;
    [Tooltip("Port for dedicated server")]
    public ushort serverPort = 7777;

    // Bot count to spawn
    public int botCount = 0;

    private bool isOfflineMode = false;
    private NetworkLobbyManager lobbyManager;

    // Dictionary to track player connections (only actual network clients)
    private Dictionary<int, NetworkConnection> playerConnections = new Dictionary<int, NetworkConnection>();

    // Awake is called when the script instance is being loaded
    public override void Awake()
    {
        base.Awake();

        if (singleton != null && singleton != this)
        {
            Destroy(gameObject);
        }
        else
        {
            singleton = this;
            DontDestroyOnLoad(gameObject);
        }

        // Check for dedicated server mode
        CheckForDedicatedServerMode();
    }

    // Start a dedicated server if needed
    private void CheckForDedicatedServerMode()
    {
        // Check command line arguments for server mode
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-server" || args[i] == "-dedicatedServer")
            {
                isDedicatedServer = true;

                // Check for port argument
                if (i + 1 < args.Length && ushort.TryParse(args[i + 1], out ushort port))
                {
                    serverPort = port;
                }
                break;
            }
        }

        // Start server if in dedicated mode
        if (isDedicatedServer)
        {
            StartDedicatedServer();
        }
    }

    // Start the dedicated server
    public void StartDedicatedServer()
    {
        Debug.Log($"Starting dedicated server on port {serverPort}");

        // Set up server configuration
        Transport transport = Transport.active;
        if (transport != null)
        {
            // Set port using reflection to handle different transport types
            var portField = transport.GetType().GetField("port");
            if (portField != null)
            {
                portField.SetValue(transport, serverPort);
            }
        }

        // Start server
        StartServer();
    }

    // Server callbacks
    public override void OnStartServer()
    {
        base.OnStartServer();

        // Spawn the appropriate manager based on scene
        SpawnSceneManager();

        Debug.Log("Server started successfully!");
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        Debug.Log("Client started successfully!");

        // Verify we have the correct player name
        if (PlayerPrefs.HasKey("PlayerName"))
        {
            playerName = PlayerPrefs.GetString("PlayerName");
        }
    }

    // Called when a server scene changes
    public override void OnServerSceneChanged(string sceneName)
    {
        base.OnServerSceneChanged(sceneName);

        // Spawn the appropriate manager based on new scene
        SpawnSceneManager();
    }

    // Called when a client scene changes
    public override void OnClientSceneChanged()
    {
        Debug.Log($"Client scene changed: {SceneManager.GetActiveScene().name}");

        // Reset any local state needed on scene change
        if (SceneManager.GetActiveScene().name == NetworkGameConfig.GAME_SCENE_NAME)
        {
            // Set local client as ready to receive spawned objects
            NetworkClient.Ready();

            if (NetworkClient.localPlayer == null)
            {
                // If there's no local player yet, request one from the server
                NetworkClient.AddPlayer();
            }
        }
    }

    // Spawn the appropriate manager based on current scene
    private void SpawnSceneManager()
    {
        string currentScene = SceneManager.GetActiveScene().name;

        if (currentScene == NetworkGameConfig.LOBBY_SCENE_NAME)
        {
            SpawnLobbyManager();
        }
        else if (currentScene == NetworkGameConfig.GAME_SCENE_NAME)
        {
            SpawnNetworkGameManager();
        }
    }

    // Spawn the lobby manager
    private void SpawnLobbyManager()
    {
        if (NetworkServer.active && lobbyManagerPrefab != null)
        {
            // Check if lobby manager already exists
            lobbyManager = FindObjectOfType<NetworkLobbyManager>();

            if (lobbyManager == null)
            {
                GameObject lobbyObj = Instantiate(lobbyManagerPrefab);
                NetworkServer.Spawn(lobbyObj);
                lobbyManager = lobbyObj.GetComponent<NetworkLobbyManager>();
                Debug.Log("Lobby manager spawned");
            }
            else
            {
                Debug.Log("Lobby manager already exists");
            }

            // Force an update of the player count
            if (lobbyManager != null)
            {
                UpdateLobbyUI();
            }
        }
    }

    // Spawn the network game manager
    private void SpawnNetworkGameManager()
    {
        if (NetworkServer.active && networkGameManagerPrefab != null)
        {
            // Check if manager already exists
            NetworkGameManager netGameManager = FindObjectOfType<NetworkGameManager>();

            if (netGameManager == null)
            {
                GameObject managerObj = Instantiate(networkGameManagerPrefab);
                NetworkServer.Spawn(managerObj);
                Debug.Log("Network game manager spawned");

                // Initialize any server-specific game state here
                GameManager gameManager = managerObj.GetComponent<GameManager>();
                if (gameManager != null)
                {
                    gameManager.isNetworkGame = true;
                }
            }
            else
            {
                Debug.Log("Network game manager already exists");
            }
        }
    }

    // Called when a player is added to the server
    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        // Call base implementation to spawn player prefab
        base.OnServerAddPlayer(conn);

        // Add to our tracking dictionary (only if not already tracked)
        if (!playerConnections.ContainsKey(conn.connectionId))
        {
            playerConnections.Add(conn.connectionId, conn);
            Debug.Log($"Added player connection {conn.connectionId}. Total players: {playerConnections.Count}");
        }

        // Set player's name if available
        NetworkSheepPlayer player = conn.identity.GetComponent<NetworkSheepPlayer>();
        if (player != null)
        {
            // Use the set player name from the manager, or generate a default
            if (!string.IsNullOrEmpty(playerName))
            {
                player.playerName = playerName;
            }
            else
            {
                player.playerName = "Player_" + conn.connectionId;
            }

            // Ensure player movement is locked at start
            PlayerController playerController = player.GetComponent<PlayerController>();
            if (playerController != null)
            {
                playerController.SetMovementLocked(true);
            }
        }

        // Immediately notify game manager that a player has joined if we're in the game scene
        if (SceneManager.GetActiveScene().name == NetworkGameConfig.GAME_SCENE_NAME &&
            GameManager.Instance != null)
        {
            GameManager.Instance.activeSheep.Add(conn.identity.gameObject);
        }

        // Update lobby UI with new player count
        UpdateLobbyUI();
    }

    // Called when a client disconnects from the server
    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        // Check for player voting status before they're removed
        if (conn.identity != null)
        {
            NetworkSheepPlayer player = conn.identity.GetComponent<NetworkSheepPlayer>();

            // Adjust votes if player voted
            if (player != null && player.hasVoted && lobbyManager != null)
            {
                lobbyManager.votesToStart--;
            }

            // Remove from game manager's active sheep list
            if (SceneManager.GetActiveScene().name == NetworkGameConfig.GAME_SCENE_NAME &&
                GameManager.Instance != null)
            {
                GameManager.Instance.activeSheep.Remove(conn.identity.gameObject);
            }
        }

        // Remove from tracking dictionary
        if (playerConnections.ContainsKey(conn.connectionId))
        {
            playerConnections.Remove(conn.connectionId);
            Debug.Log($"Removed player connection {conn.connectionId}. Remaining players: {playerConnections.Count}");
        }

        // Let Mirror handle default disconnect behavior (despawn objects, etc.)
        base.OnServerDisconnect(conn);

        // Update lobby UI with new player count
        UpdateLobbyUI();

        // Stop countdown if no players left
        if (CountConnectedPlayers() == 0 && lobbyManager != null)
        {
            // If dedicated server, reinitialize lobby
            if (isDedicatedServer)
            {
                RestartLobby();
            }
        }
    }

    // Count actual connected players (excluding the host's server role)
    public int CountConnectedPlayers()
    {
        int count = playerConnections.Count;

        // Debug info
        Debug.Log($"Player connections: {count}");

        return count;
    }

    // Update the lobby UI with current player count
    private void UpdateLobbyUI()
    {
        if (lobbyManager != null)
        {
            int playerCount = CountConnectedPlayers();
            lobbyManager.UpdatePlayerCountUI(playerCount);
        }
    }

    // Restart the lobby (for dedicated servers)
    private void RestartLobby()
    {
        if (isDedicatedServer)
        {
            Debug.Log("Restarting lobby for new players...");
            ServerChangeScene(NetworkGameConfig.LOBBY_SCENE_NAME);
        }
    }

    // Fill remaining slots with bots
    public void FillWithBots()
    {
        if (NetworkServer.active)
        {
            // Count actual connected players
            int playerCount = CountConnectedPlayers();

            // Use either the specified bot count or calculate based on player count
            int botsNeeded = 0;

            if (botCount > 0)
            {
                // Use the specified bot count directly
                botsNeeded = botCount;
            }
            else
            {
                // Calculate based on available slots
                botsNeeded = Mathf.Min(maxPlayersPerLobby - playerCount, 50); // Limit to 50 bots max
            }

            // Use this count for the game manager
            botCount = Mathf.Max(0, botsNeeded);

            Debug.Log($"Filling lobby with {botCount} bots. Human players: {playerCount}");
        }
    }

    // Start offline mode (single player with bots)
    public void StartOfflineMode()
    {
        isOfflineMode = true;

        // Load lobby scene first
        SceneManager.LoadScene(NetworkGameConfig.LOBBY_SCENE_NAME);

        // Use a coroutine to wait for scene to load
        StartCoroutine(StartOfflineModeDelayed());
    }

    private IEnumerator StartOfflineModeDelayed()
    {
        // Wait for scene to load
        yield return new WaitForSeconds(0.5f);

        // Start as host
        StartHost();

        // Set short countdown
        yield return new WaitForSeconds(0.2f);

        lobbyManager = FindObjectOfType<NetworkLobbyManager>();
        if (lobbyManager != null)
        {
            lobbyManager.currentCountdown = 3f;
        }
    }

    // Method to print debug info about connections (useful for troubleshooting)
    [ContextMenu("Debug Connection Info")]
    public void DebugConnectionInfo()
    {
        Debug.Log($"Network Info:");
        Debug.Log($"Server active: {NetworkServer.active}");
        Debug.Log($"Client active: {NetworkClient.active}");
        Debug.Log($"Is Connected: {NetworkClient.isConnected}");
        Debug.Log($"Is Host: {(NetworkServer.active && NetworkClient.active)}");
        Debug.Log($"Player Connections: {playerConnections.Count}");
        Debug.Log($"NetworkServer.connections: {NetworkServer.connections.Count}");
        Debug.Log($"Connected Players: {CountConnectedPlayers()}");

        if (NetworkClient.localPlayer != null)
        {
            Debug.Log($"Local Player: {NetworkClient.localPlayer.gameObject.name}");
        }
        else
        {
            Debug.Log("Local Player: None");
        }
    }
}