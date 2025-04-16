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

    // Dictionary to track all active player connections
    private Dictionary<int, NetworkConnection> allConnections = new Dictionary<int, NetworkConnection>();

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
        GetComponent<TelepathyTransport>().port = serverPort;

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

    // Called when a server scene changes
    public override void OnServerSceneChanged(string sceneName)
    {
        base.OnServerSceneChanged(sceneName);

        // Spawn the appropriate manager based on new scene
        SpawnSceneManager();
    }

    // Spawn the appropriate manager based on current scene
    private void SpawnSceneManager()
    {
        string currentScene = SceneManager.GetActiveScene().name;

        if (currentScene == "NetworkLobby")
        {
            SpawnLobbyManager();
        }
        else if (currentScene == "SheepBattleground")
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
            }
        }
    }

    // Called when a player is added to the server
    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        base.OnServerAddPlayer(conn);

        // Add to our tracking dictionary
        if (!allConnections.ContainsKey(conn.connectionId))
        {
            allConnections.Add(conn.connectionId, conn);
        }

        // Set player's name if available
        NetworkSheepPlayer player = conn.identity.GetComponent<NetworkSheepPlayer>();
        if (player != null)
        {
            player.playerName = "Player_" + conn.connectionId;
        }

        Debug.Log($"Player added. Total players: {CountTotalPlayers()}");
    }

    // Called when a client disconnects from the server
    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        // Remove from tracking dictionary
        if (allConnections.ContainsKey(conn.connectionId))
        {
            allConnections.Remove(conn.connectionId);
        }

        if (conn.identity != null)
        {
            NetworkSheepPlayer player = conn.identity.GetComponent<NetworkSheepPlayer>();

            // Adjust votes if player voted
            if (player != null && player.hasVoted && lobbyManager != null)
            {
                lobbyManager.votesToStart--;
            }
        }

        base.OnServerDisconnect(conn);

        // Stop countdown if no players left
        if (CountTotalPlayers() == 0 && lobbyManager != null)
        {
            // If dedicated server, reinitialize lobby
            if (isDedicatedServer)
            {
                RestartLobby();
            }
        }

        Debug.Log($"Player disconnected. Remaining players: {CountTotalPlayers()}");
    }

    // Count total players including host
    public int CountTotalPlayers()
    {
        // Count ALL player connections
        int count = allConnections.Count;

        // Add host if we're in host mode and not counted elsewhere
        if (NetworkServer.active && NetworkClient.active)
        {
            // Make sure we're not double-counting the host
            if (!NetworkClient.connection.isReady)
            {
                count++; // Add host to count if not already included
            }
        }

        return count;
    }

    // Restart the lobby (for dedicated servers)
    private void RestartLobby()
    {
        if (isDedicatedServer)
        {
            Debug.Log("Restarting lobby for new players...");
            ServerChangeScene("NetworkLobby");
        }
    }

    // Fill remaining slots with bots
    public void FillWithBots()
    {
        if (NetworkServer.active)
        {
            // Count ALL connected players including host
            int playerCount = CountTotalPlayers();
            int botsNeeded = maxPlayersPerLobby - playerCount;

            // Set bot count for the game manager to use
            botCount = Mathf.Max(0, botsNeeded);

            Debug.Log($"Filling lobby with {botCount} bots. Human players: {playerCount}");
        }
    }

    // Start offline mode (single player with bots)
    public void StartOfflineMode()
    {
        isOfflineMode = true;

        // Load lobby scene first
        SceneManager.LoadScene("NetworkLobby");

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

        if (lobbyManager != null)
        {
            lobbyManager.currentCountdown = 3f;
        }
    }

    // Method to print debug info about connections
    [ContextMenu("Debug Connection Info")]
    public void DebugConnectionInfo()
    {
        Debug.Log($"Network Info:");
        Debug.Log($"Server active: {NetworkServer.active}");
        Debug.Log($"Client active: {NetworkClient.active}");
        Debug.Log($"Is Connected: {NetworkClient.isConnected}");
        Debug.Log($"Is Host: {(NetworkServer.active && NetworkClient.active)}");
        Debug.Log($"All Connections: {allConnections.Count}");
        Debug.Log($"NetworkServer.connections: {NetworkServer.connections.Count}");
        Debug.Log($"Total Players (Method): {CountTotalPlayers()}");
    }
}