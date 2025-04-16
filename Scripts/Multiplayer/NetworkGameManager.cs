using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

// This class should be attached to the GameManager object
[RequireComponent(typeof(GameManager))]
[RequireComponent(typeof(NetworkBotSpawner))]
public class NetworkGameManager : NetworkBehaviour
{
    // Reference to the base game manager
    private GameManager gameManager;
    private NetworkBotSpawner botSpawner;

    // Network properties
    [SyncVar(hook = nameof(OnGameStartedChanged))]
    private bool networkGameStarted = false;

    [SyncVar(hook = nameof(OnRemainingCountChanged))]
    private int remainingSheepCount = 0;

    private void Awake()
    {
        // Get reference to the game manager
        gameManager = GetComponent<GameManager>();
        botSpawner = GetComponent<NetworkBotSpawner>();

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

        // Listen to game manager events
        StartCoroutine(MonitorGameManager());
    }

    // Coroutine to monitor game state changes on server
    private IEnumerator MonitorGameManager()
    {
        yield return new WaitForSeconds(1f); // Wait for setup

        int lastCount = 0;
        bool lastGameOver = false;

        while (true)
        {
            // Check if sheep count has changed
            if (gameManager.activeSheep != null && gameManager.activeSheep.Count != lastCount)
            {
                lastCount = gameManager.activeSheep.Count;
                remainingSheepCount = lastCount;
            }

            // Check if game state has changed
            if (gameManager.gameStarted && !networkGameStarted)
            {
                networkGameStarted = true;

                // Notify all clients that game has started
                RpcGameStarted();
            }

            // Check if game over state has changed
            if (gameManager.gameOver && !lastGameOver)
            {
                lastGameOver = true;
                RpcNotifyGameOver(gameManager.playerSheep != null); // True if player alive (win)
            }

            yield return new WaitForSeconds(0.2f); // Check every 0.2 seconds
        }
    }

    // Called when the game started state changes
    void OnGameStartedChanged(bool oldValue, bool newValue)
    {
        if (newValue && !oldValue && !isServer)
        {
            // Game started notification for clients
            Debug.Log("Network game has started!");

            // Update local game state
            if (gameManager != null)
            {
                gameManager.gameStarted = true;
            }

            // Unlock all sheep movement
            UnlockAllSheepMovement();
        }
    }

    // Called when the remaining sheep count changes
    void OnRemainingCountChanged(int oldValue, int newValue)
    {
        if (!isServer && gameManager != null)
        {
            // Update UI on clients
            if (gameManager.sheepRemainingText != null)
            {
                gameManager.sheepRemainingText.text = $"Sheep Remaining: {newValue}";
            }
        }
    }

    // RPC to notify all clients the game has started
    [ClientRpc]
    void RpcGameStarted()
    {
        if (isServer) return; // Server already handled this

        Debug.Log("RPC: Game started!");

        // Update local game state
        if (gameManager != null)
        {
            gameManager.gameStarted = true;
        }

        // Unlock all sheep movement
        UnlockAllSheepMovement();
    }

    // Unlock movement for all sheep
    void UnlockAllSheepMovement()
    {
        // Find and unlock all player controllers
        PlayerController[] players = FindObjectsOfType<PlayerController>();
        foreach (PlayerController player in players)
        {
            player.SetMovementLocked(false);
        }

        // Find and unlock all AI controllers
        AIPlayerController[] aiPlayers = FindObjectsOfType<AIPlayerController>();
        foreach (AIPlayerController ai in aiPlayers)
        {
            ai.SetMovementLocked(false);
        }

        Debug.Log("All sheep movement unlocked");
    }

    // RPC to notify all clients about game over
    [ClientRpc]
    void RpcNotifyGameOver(bool playerWon)
    {
        if (isServer) return; // Server already handled this

        // Show appropriate UI
        if (playerWon)
        {
            if (gameManager.victoryPanel != null)
            {
                gameManager.victoryPanel.SetActive(true);
            }

            // Play random victory sound
            gameManager.PlayRandomSound(gameManager.victorySounds);
        }
        else
        {
            if (gameManager.defeatPanel != null)
            {
                gameManager.defeatPanel.SetActive(true);
            }

            // Play random defeat sound
            gameManager.PlayRandomSound(gameManager.defeatSounds);
        }

        // Show restart button
        if (gameManager.restartButton != null)
        {
            gameManager.restartButton.gameObject.SetActive(true);
        }

        if (gameManager.mainMenuButton != null)
        {
            gameManager.mainMenuButton.gameObject.SetActive(true);
        }
    }
}