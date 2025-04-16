using Mirror;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NetworkBotSpawner : NetworkBehaviour
{
    [Header("Bot Spawning")]
    public int totalBotsToSpawn = 0;
    public GameObject networkAIBotPrefab;

    [SyncVar(hook = nameof(OnBotsSpawnedChanged))]
    private bool botsSpawned = false;

    private GameManager gameManager;

    void Awake()
    {
        gameManager = GetComponent<GameManager>();
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

        // Calculate bots needed (max 100 total players including humans)
        totalBotsToSpawn = Mathf.Max(0, 100 - humanPlayers);

        Debug.Log($"Network game will have {humanPlayers} human players and {totalBotsToSpawn} bots");
    }

    // Wait for the game to start before spawning bots
    IEnumerator WaitForGameStart()
    {
        if (!isServer) yield break;

        // Wait for the game to start (countdown finished)
        while (gameManager != null && !gameManager.gameStarted)
        {
            yield return new WaitForSeconds(0.1f);
        }

        // Spawn bots
        if (!botsSpawned)
        {
            SpawnBots();
        }
    }

    // Spawn the bots on server
    void SpawnBots()
    {
        if (!isServer || botsSpawned) return;

        Debug.Log($"Spawning {totalBotsToSpawn} bots across the network");

        for (int i = 0; i < totalBotsToSpawn; i++)
        {
            SpawnNetworkBot();

            // Small delay to avoid physics glitches from spawning too many at once
            if (i % 5 == 0)
            {
                // Allow a frame to process between batches
                // (We can't yield in a non-coroutine, but we only need small pauses)
                System.Threading.Thread.Sleep(10);
            }
        }

        // Mark bots as spawned to avoid duplicate spawning
        botsSpawned = true;
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

        // Make sure AI can move after spawn if game is already started
        AIPlayerController aiController = botObject.GetComponent<AIPlayerController>();
        if (aiController != null && gameManager.gameStarted)
        {
            aiController.SetMovementLocked(false);
        }
    }

    // Hook called when bots spawned state changes
    void OnBotsSpawnedChanged(bool oldValue, bool newValue)
    {
        if (newValue)
        {
            Debug.Log("Bots spawned successfully across the network");
        }
    }
}