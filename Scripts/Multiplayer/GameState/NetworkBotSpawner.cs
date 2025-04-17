using Mirror;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NetworkBotSpawner : NetworkBehaviour
{
    [Header("Bot Spawning")]
    [Tooltip("This will be filled automatically based on player count")]
    [SyncVar]
    public int totalBotsToSpawn = 0;

    public GameObject networkAIBotPrefab;

    [SyncVar(hook = nameof(OnBotsSpawnedChanged))]
    private bool botsSpawned = false;

    private GameManager gameManager;

    void Awake()
    {
        gameManager = GetComponent<GameManager>();

        if (gameManager == null)
        {
            Debug.LogError("NetworkBotSpawner requires GameManager component");
            enabled = false;
            return;
        }
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        // Calculate bots when the server starts
        CalculateBotCount();

        // Spawn bots when game starts
        StartCoroutine(WaitForGameStart());
    }

    // Calculate how many bots to spawn
    void CalculateBotCount()
    {
        if (!isServer) return;

        // Get bot count from network manager if available
        SheepNetworkManager networkManager = SheepNetworkManager.singleton;
        if (networkManager != null)
        {
            totalBotsToSpawn = networkManager.botCount;
            Debug.Log($"Using bot count from NetworkManager: {totalBotsToSpawn}");
        }
        else
        {
            // Fallback if network manager reference is not available
            int humanPlayers = NetworkServer.connections.Count;

            // Ensure host is counted
            if (NetworkClient.active && NetworkServer.active)
            {
                // Double check we're not counting host twice
                bool hostIncluded = false;
                foreach (var conn in NetworkServer.connections.Values)
                {
                    if (conn.connectionId == 0) // Host connection ID
                    {
                        hostIncluded = true;
                        break;
                    }
                }

                if (!hostIncluded)
                {
                    humanPlayers++;
                }
            }

            // Calculate bots needed (max 20 total bots)
            totalBotsToSpawn = Mathf.Min(20, Mathf.Max(0, 25 - humanPlayers));
            Debug.Log($"Calculated bot count: {totalBotsToSpawn} (for {humanPlayers} human players)");
        }
    }

    // Wait for the game to start before spawning bots
    IEnumerator WaitForGameStart()
    {
        if (!isServer) yield break;

        // Wait for the game to start (countdown finished)
        while (gameManager != null && !gameManager.gameStarted)
        {
            yield return new WaitForSeconds(0.01f);
        }

        // Spawn bots if needed
        if (!botsSpawned && totalBotsToSpawn > 0)
        {
            yield return StartCoroutine(SpawnBotsCoroutine());
        }
    }

    // Spawn the bots on server - using coroutine to avoid freezing with many bots
    IEnumerator SpawnBotsCoroutine()
    {
        if (!isServer || botsSpawned) yield break;

        Debug.Log($"Spawning {totalBotsToSpawn} bots across the network");

        // Clear any existing bots (just in case)
        ClearExistingBots();

        for (int i = 0; i < totalBotsToSpawn; i++)
        {
            SpawnNetworkBot();

            // Every 5 bots, wait a frame to prevent freezing
            if (i % 5 == 4)
            {
                yield return null;
            }
        }

        // Mark bots as spawned to avoid duplicate spawning
        botsSpawned = true;

        // Now update the game manager with remaining sheep
        if (gameManager != null)
        {
            RpcSyncActiveSheepCount(gameManager.activeSheep.Count);
        }
    }

    // Clear any existing bots before spawning new ones
    void ClearExistingBots()
    {
        if (!isServer) return;

        // Find and remove any existing network AI bots
        NetworkAISheep[] existingBots = FindObjectsOfType<NetworkAISheep>();

        foreach (NetworkAISheep bot in existingBots)
        {
            // Remove from active sheep list
            if (gameManager != null && gameManager.activeSheep.Contains(bot.gameObject))
            {
                gameManager.activeSheep.Remove(bot.gameObject);
            }

            // Destroy the bot object
            NetworkServer.Destroy(bot.gameObject);
        }

        Debug.Log($"Cleared {existingBots.Length} existing bots");
    }

    // Spawn a single network bot
    void SpawnNetworkBot()
    {
        if (!isServer || networkAIBotPrefab == null) return;

        // Get a spawn position from game manager
        Vector3 spawnPos = gameManager.GetRandomSpawnPosition();

        // Random rotation
        Quaternion randomRot = Quaternion.Euler(0, Random.Range(0, 360), 0);

        // Spawn the bot with network visibility
        GameObject botObject = Instantiate(networkAIBotPrefab, spawnPos, randomRot);

        // Spawn on network
        NetworkServer.Spawn(botObject);

        // Add to game manager's active sheep
        gameManager.activeSheep.Add(botObject);

        // Make sure AI knows we're in network mode
        NetworkAISheep networkAI = botObject.GetComponent<NetworkAISheep>();
        if (networkAI != null)
        {
            // Set any network-specific properties
            networkAI.isNetworkBot = true;
        }

        // Make sure AI can move after spawn if game is already started
        AIPlayerController aiController = botObject.GetComponent<AIPlayerController>();
        if (aiController != null)
        {
            aiController.isNetworkMode = true;

            if (gameManager.gameStarted)
            {
                aiController.SetMovementLocked(false);
            }
            else
            {
                aiController.SetMovementLocked(true);
            }
        }
    }

    // Hook called when bots spawned state changes
    void OnBotsSpawnedChanged(bool oldValue, bool newValue)
    {
        if (newValue)
        {
            Debug.Log("Bots spawned successfully across the network");

            // On clients, update the sheep count UI
            if (!isServer && gameManager != null && gameManager.sheepRemainingText != null)
            {
                // Find all active sheep to update the count
                int totalSheep = 0;
                NetworkSheepPlayer[] players = FindObjectsOfType<NetworkSheepPlayer>();
                NetworkAISheep[] aiSheep = FindObjectsOfType<NetworkAISheep>();

                totalSheep = players.Length + aiSheep.Length;
                gameManager.sheepRemainingText.text = $"Sheep Remaining: {totalSheep}";
            }
        }
    }

    // Sync the active sheep count to clients
    [ClientRpc]
    void RpcSyncActiveSheepCount(int count)
    {
        if (!isServer && gameManager != null && gameManager.sheepRemainingText != null)
        {
            gameManager.sheepRemainingText.text = $"Sheep Remaining: {count}";
        }
    }

    // Public method to manually trigger bot spawning
    public void RequestBotSpawn(int count)
    {
        if (!isServer) return;

        // Only allow this if bots haven't been spawned yet
        if (!botsSpawned)
        {
            totalBotsToSpawn = count;
            StartCoroutine(SpawnBotsCoroutine());
        }
        else
        {
            Debug.LogWarning("Cannot spawn more bots - bots already spawned");
        }
    }

    // Public method to check if we've spawned bots
    public bool HaveBotsBeenSpawned()
    {
        return botsSpawned;
    }
}