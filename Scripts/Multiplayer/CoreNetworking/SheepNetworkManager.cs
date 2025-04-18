using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using UnityEngine.SceneManagement;

// Modified to extend NetworkRoomManager instead of NetworkManager
public class SheepNetworkManager : NetworkRoomManager
{
    [Header("Player Settings")]
    public string playerName = "Sheep";

    [Header("Prefabs")]
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

    [Header("Room Settings")]
    public bool autoStartCountdown = true;
    public bool botSettingsFromLobby = true;
    public float roomPlayerReadyTimeout = 5f;

    private bool isOfflineMode = false;
    private NetworkLobbyManager lobbyManager;

    // Dictionary to track player connections (only actual network clients)
    private Dictionary<int, NetworkConnection> playerConnections = new Dictionary<int, NetworkConnection>();

    // Awake is called when the script instance is being loaded
    public override void Awake()
    {
        base.Awake();

        // Check for dedicated server mode
        CheckForDedicatedServerMode();

        // Set the game and room scene names
        RoomScene = NetworkGameConfig.LOBBY_SCENE_NAME;
        GameplayScene = NetworkGameConfig.GAME_SCENE_NAME;
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

    // Server callbacks - we can reuse these from the base NetworkRoomManager
    public override void OnStartServer()
    {
        base.OnStartServer();
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

    // Override to add custom server logic when a player joins
    public override GameObject OnRoomServerCreateRoomPlayer(NetworkConnectionToClient conn)
    {
        // First, create the room player using the base method
        GameObject roomPlayerObj = base.OnRoomServerCreateRoomPlayer(conn);
        
        if (roomPlayerObj != null)
        {
            // Add to our tracking dictionary (only if not already tracked)
            if (!playerConnections.ContainsKey(conn.connectionId))
            {
                playerConnections.Add(conn.connectionId, conn);
                Debug.Log($"Added player connection {conn.connectionId}. Total players: {playerConnections.Count}");
            }
            
            // Set the player name if we have a NetworkRoomPlayerSheep component
            NetworkRoomPlayerSheep roomPlayer = roomPlayerObj.GetComponent<NetworkRoomPlayerSheep>();
            if (roomPlayer != null)
            {
                // Set initial player name (can be updated by client later)
                roomPlayer.playerName = "Player_" + conn.connectionId;
            }
        }
        
        return roomPlayerObj;
    }

    // Override to add custom server logic when creating a game player
    public override GameObject OnRoomServerCreateGamePlayer(NetworkConnectionToClient conn, GameObject roomPlayer)
    {
        // Get a spawn position for the player
        Vector3 spawnPosition = Vector3.zero;
        GameManager gameManager = GameManager.Instance;
        if (gameManager != null)
        {
            spawnPosition = gameManager.GetRandomSpawnPosition();
        }

        // Instantiate the player at the spawn position
        GameObject playerObj = Instantiate(playerPrefab, spawnPosition, Quaternion.identity);

        // Configure the player
        NetworkSheepPlayer player = playerObj.GetComponent<NetworkSheepPlayer>();
        if (player != null)
        {
            player.playerName = playerName;

            // Ensure player movement is locked at start
            PlayerController playerController = player.GetComponent<PlayerController>();
            if (playerController != null)
            {
                playerController.SetMovementLocked(true);
            }
        }

        // Add to game manager's active sheep list
        if (gameManager != null)
        {
            gameManager.activeSheep.Add(playerObj);
        }

        return playerObj;
    }

    // Called when all players in the room are ready
    public override void OnRoomServerPlayersReady()
    {
        // Start game with countdown when all players are ready
        base.OnRoomServerPlayersReady();

        // Find the NetworkLobbyManager
        NetworkLobbyManager lobbyManager = FindObjectOfType<NetworkLobbyManager>();
        if (lobbyManager != null)
        {
            // Let the lobby manager start the countdown
            lobbyManager.StartCountdown();
        }
        else
        {
            // Fallback if no lobby manager - change scene directly
            double countdownTime = roomPlayerReadyTimeout;
            if (countdownTime <= 0)
            {
                countdownTime = 2;
            }
            Invoke(nameof(ServerChangeScene), (float)countdownTime);
        }
    }

    // Called when a client disconnects
    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        // Remove from tracking dictionary
        if (playerConnections.ContainsKey(conn.connectionId))
        {
            playerConnections.Remove(conn.connectionId);
            Debug.Log($"Removed player connection {conn.connectionId}. Remaining players: {playerConnections.Count}");
        }

        // Let Mirror handle default disconnect behavior
        base.OnServerDisconnect(conn);
    }

    // Called after a server scene change
    public override void OnRoomServerSceneChanged(string sceneName)
    {
        base.OnRoomServerSceneChanged(sceneName);
        
        if (sceneName == NetworkGameConfig.GAME_SCENE_NAME)
        {
            Debug.Log("Game scene loaded - initializing network game");
            
            // Find the GameManager
            GameManager gameManager = FindObjectOfType<GameManager>();
            if (gameManager != null)
            {
                Debug.Log("Found GameManager - configuring for network play");
                gameManager.isNetworkGame = true;
                
                // Calculate bots needed (99 if one player, 98 if two players, etc.)
                int humanPlayerCount = CountConnectedPlayers();
                int botsNeeded = 100 - humanPlayerCount;
                
                Debug.Log($"Spawning {botsNeeded} bots for {humanPlayerCount} human players");
                gameManager.SpawnNetworkedSheep(botsNeeded);
            }
            else
            {
                Debug.LogError("Could not find GameManager in game scene!");
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
                GameManager gameManager = FindObjectOfType<GameManager>();
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

    // Spawn bots for the game
    private void SpawnBots()
    {
        if (!NetworkServer.active)
            return;

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
            int playerCount = CountConnectedPlayers();
            botsNeeded = Mathf.Min(maxPlayersPerLobby - playerCount, 50); // Limit to 50 bots max
        }

        // Spawn the bots
        Debug.Log($"Spawning {botsNeeded} bots");
        GameManager gameManager = GameManager.Instance;
        if (gameManager != null)
        {
            gameManager.SpawnNetworkedSheep(botsNeeded);
        }
    }

    // Count actual connected players
    public int CountConnectedPlayers()
    {
        // First check if we're the host - we always count as a player
        bool isHost = NetworkServer.active && NetworkClient.active;
        int hostCount = isHost ? 1 : 0;
        
        // Get connected client count - if we're host, we're both server and client, so avoid double-counting
        int clientCount = playerConnections.Count;
        
        // Calculate total players
        int totalPlayers = isHost ? Mathf.Max(hostCount, clientCount) : clientCount;
        
        Debug.Log($"Counting players: Host={isHost}, HostCount={hostCount}, ClientCount={clientCount}, TotalPlayers={totalPlayers}");
        
        return totalPlayers;
    }

    // Start offline mode (single player with bots)
    public void StartOfflineMode()
    {
        isOfflineMode = true;

        // Start as host
        StartHost();

        // This will automatically take us through the room scene to the game scene
    }

    public void ConfigureBotSettings()
    {
        // Count how many human players we have
        int humanPlayerCount = CountConnectedPlayers();
        
        // Calculate how many bots we need for a total of 100 sheep
        int totalDesiredSheep = 100;
        int botsNeeded = Mathf.Max(0, totalDesiredSheep - humanPlayerCount);
        
        // Set the bot count
        botCount = botsNeeded;
        
        Debug.Log($"Configured game with {humanPlayerCount} human players and {botsNeeded} bots for a total of {totalDesiredSheep} sheep");
    }

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