using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Mirror;

public class GameManager : MonoBehaviour
{
    // Singleton pattern
    public static GameManager Instance { get; private set; }

    [Header("Game Settings")]
    public int initialSheepCount = 10; // Total number of sheep (including player)
    public float startDelay = 3f; // Time before battle begins

    [Header("Arena Settings")]
    public Transform arena;
    public Vector3 arenaSize = new Vector3(20, 2, 20);
    public float boundaryForce = 20f;

    [Header("Prefabs")]
    public GameObject playerSheepPrefab;
    public GameObject aiSheepPrefab;

    [Header("UI References")]
    public Text countdownText;
    public Text sheepRemainingText;
    public GameObject victoryPanel;
    public GameObject defeatPanel;
    public Button restartButton;
    public Button mainMenuButton; // New button to return to main menu

    [Header("Audio")]
    [Tooltip("Random sound played when the match begins")]
    public AudioClip[] startGameSounds;
    [Tooltip("Random sound played when player wins")]
    public AudioClip[] victorySounds;
    [Tooltip("Random sound played when player loses")]
    public AudioClip[] defeatSounds;
    [Range(0, 1)]
    public float soundVolume = 1.0f;

    [Header("Network Settings")]
    public bool isNetworkGame = false;
    public bool playersOnlyMode = false; // Only players count for victory conditions in network mode

    // Game state
    public List<GameObject> activeSheep = new List<GameObject>();
    public GameObject playerSheep;
    public bool gameStarted = false;
    public bool gameOver = false;
    private int remainingSheepCount = 0;
    private AudioSource audioSource;
    private string playerName = "Player";

    // Track if we need to update the sheep count
    private bool sheepCountDirty = false;

    // Debug visualization for arena boundaries
    public bool debugVisualizeArena = true;

    void Awake()
    {
        // Singleton setup
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }

        // Get or add audio source
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        // Check if this is a network game
        if (NetworkServer.active || NetworkClient.active)
        {
            isNetworkGame = true;

            // Get the player name from PlayerPrefs
            if (PlayerPrefs.HasKey("PlayerName"))
            {
                playerName = PlayerPrefs.GetString("PlayerName");
            }
        }
    }

    void Start()
    {
        // Hide end game UI
        if (victoryPanel != null) victoryPanel.SetActive(false);
        if (defeatPanel != null) defeatPanel.SetActive(false);

        // Setup restart button
        if (restartButton != null)
        {
            restartButton.onClick.AddListener(RestartGame);
            restartButton.gameObject.SetActive(false);
        }

        // Setup main menu button
        if (mainMenuButton != null)
        {
            mainMenuButton.onClick.AddListener(ReturnToMainMenu);
            mainMenuButton.gameObject.SetActive(false);
        }

        // Start the game initialization
        if (!isNetworkGame)
        {
            // Only auto-initialize for offline games
            StartCoroutine(InitializeGame());
        }
    }
    
    public void InitializeNetworkGame(bool playersOnlyVictory = false)
    {
        if (!isNetworkGame) return;

        Debug.Log("Initializing network game with playersOnlyMode=" + playersOnlyVictory);
        
        // Set game mode
        playersOnlyMode = playersOnlyVictory;
        
        // Make sure the game isn't considered started yet
        gameStarted = false;
        gameOver = false;
        
        // Reset any game state needed
        if (activeSheep == null)
        {
            activeSheep = new List<GameObject>();
        }
        else
        {
            activeSheep.Clear();
        }
        
        // Find local player and add it to active sheep list
        if (NetworkClient.localPlayer != null)
        {
            GameObject localPlayerObj = NetworkClient.localPlayer.gameObject;
            if (!activeSheep.Contains(localPlayerObj))
            {
                activeSheep.Add(localPlayerObj);
                Debug.Log("Added local player to active sheep list: " + localPlayerObj.name);
                
                // Set reference to player's sheep
                playerSheep = localPlayerObj;
            }
        }
        
        // Find other network players and add them
        NetworkSheepPlayer[] allNetworkPlayers = FindObjectsOfType<NetworkSheepPlayer>();
        foreach (NetworkSheepPlayer netPlayer in allNetworkPlayers)
        {
            if (!activeSheep.Contains(netPlayer.gameObject))
            {
                activeSheep.Add(netPlayer.gameObject);
                Debug.Log("Added network player to active sheep list: " + netPlayer.gameObject.name);
            }
        }
        
        // Find network AI sheep and add them
        NetworkAISheep[] aiSheep = FindObjectsOfType<NetworkAISheep>();
        foreach (NetworkAISheep ai in aiSheep)
        {
            if (!activeSheep.Contains(ai.gameObject))
            {
                activeSheep.Add(ai.gameObject);
                Debug.Log("Added network AI to active sheep list: " + ai.gameObject.name);
            }
        }
        
        // Hide end game UI
        if (victoryPanel != null) victoryPanel.SetActive(false);
        if (defeatPanel != null) defeatPanel.SetActive(false);
        
        // Hide buttons
        if (restartButton != null) restartButton.gameObject.SetActive(false);
        if (mainMenuButton != null) mainMenuButton.gameObject.SetActive(false);
        
        // Update UI with initial count
        remainingSheepCount = activeSheep.Count;
        UpdateSheepCountUI();
        Debug.Log("Initial sheep count: " + remainingSheepCount);
        
        // Start countdown to game start
        StartCoroutine(NetworkGameCountdown());
    }

    // Network game countdown
    private IEnumerator NetworkGameCountdown()
    {
        // Show countdown
        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(true);
        }

        // Set initial sheep count
        remainingSheepCount = activeSheep.Count;
        UpdateSheepCountUI();

        // Countdown sequence
        for (int i = Mathf.CeilToInt(startDelay); i > 0; i--)
        {
            if (countdownText != null)
            {
                countdownText.text = i.ToString();
            }
            yield return new WaitForSeconds(1f);
        }

        // Start the game
        if (countdownText != null)
        {
            countdownText.text = "GO!";

            // Play random start sound
            PlayRandomSound(startGameSounds);

            yield return new WaitForSeconds(1f);
            countdownText.gameObject.SetActive(false);
        }

        gameStarted = true;

        // Trigger game started event
        GameEvents.TriggerGameStarted();

        // Enable all sheep movement
        EnableAllSheep();
    }

    IEnumerator InitializeGame()
    {
        // Show countdown
        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(true);
        }

        // Spawn local player first
        SpawnLocalPlayer();

        // Spawn AI sheep
        for (int i = 0; i < initialSheepCount - 1; i++) // -1 because player is already spawned
        {
            SpawnAISheep();
            yield return new WaitForSeconds(0.1f); // Small delay between spawns for performance
        }

        // Set initial sheep count
        remainingSheepCount = activeSheep.Count;
        UpdateSheepCountUI();

        // Countdown sequence
        for (int i = Mathf.CeilToInt(startDelay); i > 0; i--)
        {
            if (countdownText != null)
            {
                countdownText.text = i.ToString();
            }
            yield return new WaitForSeconds(1f);
        }

        // Start the game
        if (countdownText != null)
        {
            countdownText.text = "GO!";

            // Play random start sound
            PlayRandomSound(startGameSounds);

            yield return new WaitForSeconds(1f);
            countdownText.gameObject.SetActive(false);
        }

        gameStarted = true;

        // Trigger game started event
        GameEvents.TriggerGameStarted();

        // Enable all sheep movement
        EnableAllSheep();
    }

    // Helper method to play a random sound from an array
    public void PlayRandomSound(AudioClip[] sounds)
    {
        if (sounds == null || sounds.Length == 0 || audioSource == null)
            return;

        AudioClip selectedClip = sounds[Random.Range(0, sounds.Length)];
        if (selectedClip != null)
        {
            audioSource.PlayOneShot(selectedClip, soundVolume);
        }
    }

    void SpawnLocalPlayer()
    {
        // Get arena center
        Vector3 arenaCenter = GetArenaCenter();

        // Spawn player at random position near center
        Vector3 spawnPos = GetRandomSpawnPosition(false);
        playerSheep = Instantiate(playerSheepPrefab, spawnPos, Quaternion.identity);

        // Add to active sheep list
        activeSheep.Add(playerSheep);

        // Initially disable player control
        PlayerController playerController = playerSheep.GetComponent<PlayerController>();
        if (playerController != null)
        {
            playerController.SetMovementLocked(true);
        }
    }

    void SpawnAISheep()
    {
        // Get a random position
        Vector3 spawnPos = GetRandomSpawnPosition(true);

        // Random rotation
        Quaternion randomRot = Quaternion.Euler(0, Random.Range(0, 360), 0);

        // Spawn AI sheep
        GameObject newSheep = Instantiate(aiSheepPrefab, spawnPos, randomRot);

        // Set sheep tag
        newSheep.tag = "Sheep";

        // Disable AI controller initially
        AIPlayerController aiController = newSheep.GetComponent<AIPlayerController>();
        if (aiController != null)
        {
            aiController.enabled = false;
        }

        // Add to active sheep list
        activeSheep.Add(newSheep);
    }

    public void EnableAllSheep()
    {
        // Enable all sheep controllers
        foreach (GameObject sheep in activeSheep)
        {
            if (sheep == null) continue;

            PlayerController playerController = sheep.GetComponent<PlayerController>();
            if (playerController != null)
            {
                playerController.enabled = true;
                playerController.SetMovementLocked(false);
            }

            AIPlayerController aiController = sheep.GetComponent<AIPlayerController>();
            if (aiController != null)
            {
                aiController.enabled = true;
                aiController.SetMovementLocked(false);
            }

            // For network sheep, ensure controllers are enabled
            NetworkSheepPlayer netPlayer = sheep.GetComponent<NetworkSheepPlayer>();
            if (netPlayer != null)
            {
                PlayerController netPlayerController = sheep.GetComponent<PlayerController>();
                if (netPlayerController != null)
                {
                    netPlayerController.enabled = true;
                    netPlayerController.SetMovementLocked(false);
                }
            }

            NetworkAISheep networkAI = sheep.GetComponent<NetworkAISheep>();
            if (networkAI != null)
            {
                AIPlayerController networkAIController = sheep.GetComponent<AIPlayerController>();
                if (networkAIController != null)
                {
                    networkAIController.enabled = true;
                    networkAIController.SetMovementLocked(false);
                }
            }
        }
    }

    void Update()
    {

        if (isNetworkGame && NetworkServer.active)
        {
            // Fix for zero sheep count before game fully starts
            if (activeSheep.Count == 0 && !gameOver && !gameStarted)
            {
                Debug.LogWarning("Detected zero sheep count before game started. This shouldn't happen!");
                return; // Skip the rest of Update to prevent premature game over
            }
        }

        if (!gameStarted || gameOver) return;

        // Check if game is over
        CheckGameEndConditions();

        // Check arena boundaries for all sheep
        foreach (GameObject sheep in activeSheep)
        {
            if (sheep == null) continue;

            // Call separate method to apply boundaries
            EnforceArenaBoundaries(sheep);
        }

        // Update sheep count if changed
        if (sheepCountDirty)
        {
            UpdateSheepCountUI();
            sheepCountDirty = false;
        }
    }

    void OnDrawGizmos()
    {
        if (debugVisualizeArena)
        {
            // Draw arena boundaries for debugging
            Vector3 center = GetArenaCenter();
            Vector3 size = GetArenaSize();

            // Set color
            Gizmos.color = Color.red;

            // Draw wireframe cube for arena boundaries
            Gizmos.DrawWireCube(center, size);
        }
    }

    void LateUpdate()
    {
        // Clean up any null references in the activeSheep list
        activeSheep.RemoveAll(sheep => sheep == null);
    }

    void CheckGameEndConditions()
    {
        // First remove any null references (sheep that might have been destroyed)
        activeSheep.RemoveAll(sheep => sheep == null);

        // Update remaining count
        int currentCount = activeSheep.Count;
        if (currentCount != remainingSheepCount)
        {
            remainingSheepCount = currentCount;
            sheepCountDirty = true;
        }

        // Check if player is still alive
        bool playerAlive = false;
        if (playerSheep != null && playerSheep.activeSelf)
        {
            playerAlive = true;
        }

        // For network games, let NetworkGameManager handle game end conditions
        if (isNetworkGame)
        {
            // Handle local player death
            if (!playerAlive && !gameOver && playerSheep != null)
            {
                playerSheep = null;

                // Show defeat panel for this player
                if (defeatPanel != null)
                {
                    defeatPanel.SetActive(true);

                    // Show main menu button
                    if (mainMenuButton != null)
                    {
                        mainMenuButton.gameObject.SetActive(true);
                    }
                }

                // Play random defeat sound
                PlayRandomSound(defeatSounds);
            }
            return;
        }

        // Standard single player end conditions
        if (!playerAlive)
        {
            // Player died
            EndGame(false);
        }
        else if (activeSheep.Count == 1 && playerAlive)
        {
            // Player is the only one left
            EndGame(true);
        }
    }

    void EndGame(bool playerWon)
    {
        gameOver = true;

        // Stop all movement
        if (playerSheep != null)
        {
            Rigidbody playerRb = playerSheep.GetComponent<Rigidbody>();
            if (playerRb != null)
            {
                playerRb.velocity = Vector3.zero;
                playerRb.angularVelocity = Vector3.zero;
            }
        }

        // Show appropriate UI
        if (playerWon)
        {
            if (victoryPanel != null)
            {
                victoryPanel.SetActive(true);
            }

            // Play random victory sound
            PlayRandomSound(victorySounds);
        }
        else
        {
            if (defeatPanel != null)
            {
                defeatPanel.SetActive(true);
            }

            // Play random defeat sound
            PlayRandomSound(defeatSounds);
        }

        // Show buttons
        if (restartButton != null)
        {
            restartButton.gameObject.SetActive(true);
        }

        if (mainMenuButton != null)
        {
            mainMenuButton.gameObject.SetActive(true);
        }

        // Trigger game over event
        GameEvents.TriggerGameOver(playerWon);
    }

    public void UpdateSheepCountUI()
    {
        if (sheepRemainingText != null)
        {
            sheepRemainingText.text = "Sheep Remaining: " + remainingSheepCount;
        }

        // In network mode, sync to all clients
        if (isNetworkGame && NetworkServer.active)
        {
            NetworkGameManager networkGameManager = FindObjectOfType<NetworkGameManager>();
            if (networkGameManager != null)
            {
                networkGameManager.RpcUpdateSheepCount(remainingSheepCount);
            }
        }
    }

    void EnforceArenaBoundaries(GameObject sheep)
    {
        // Skip if sheep is null
        if (sheep == null) return;

        // Get the rigidbody
        Rigidbody rb = sheep.GetComponent<Rigidbody>();
        if (rb == null) return;

        // In network mode, check if this is a host player with authority
        bool isHostWithAuthority = false;
        NetworkSheepPlayer netPlayer = sheep.GetComponent<NetworkSheepPlayer>();
        if (isNetworkGame && netPlayer != null && netPlayer.isLocalPlayer && NetworkServer.active)
        {
            isHostWithAuthority = true;
        }

        // Get arena center and boundaries
        Vector3 centerPos = GetArenaCenter();
        Vector3 sheepToCenter = centerPos - sheep.transform.position;
        sheepToCenter.y = 0; // Keep it on horizontal plane

        // Get arena dimensions
        float halfWidth = arenaSize.x / 2;
        float halfLength = arenaSize.z / 2;

        // Check if beyond boundary on X axis
        bool outsideX = Mathf.Abs(sheep.transform.position.x - centerPos.x) > halfWidth;
        // Check if beyond boundary on Z axis
        bool outsideZ = Mathf.Abs(sheep.transform.position.z - centerPos.z) > halfLength;

        // If either axis is outside the boundary
        if (outsideX || outsideZ)
        {
            // Calculate force direction towards center
            Vector3 forceDirection = Vector3.zero;

            // For X axis
            if (outsideX)
            {
                float distanceX = Mathf.Abs(sheep.transform.position.x - centerPos.x) - halfWidth;
                float signX = Mathf.Sign(centerPos.x - sheep.transform.position.x);
                forceDirection.x = signX * distanceX;
            }

            // For Z axis
            if (outsideZ)
            {
                float distanceZ = Mathf.Abs(sheep.transform.position.z - centerPos.z) - halfLength;
                float signZ = Mathf.Sign(centerPos.z - sheep.transform.position.z);
                forceDirection.z = signZ * distanceZ;
            }

            // Normalize and apply force - use stronger force for host player
            float forceMagnitude = boundaryForce;
            if (isHostWithAuthority)
            {
                forceMagnitude *= 2.0f; // Apply stronger force to host player
            }

            // Apply force towards center
            rb.AddForce(forceDirection.normalized * forceMagnitude, ForceMode.Force);

            // Debug visualize
            Debug.DrawLine(sheep.transform.position,
                          sheep.transform.position + forceDirection.normalized * 3f,
                          Color.red, 0.1f);
        }
    }

    public void SheepDied(GameObject sheep)
    {
        // Log for debugging
        Debug.Log($"Sheep died: {sheep.name}");

        // Remove from active list
        if (activeSheep.Contains(sheep))
        {
            activeSheep.Remove(sheep);
            sheepCountDirty = true;

            // Trigger the sheep death event
            GameEvents.TriggerSheepDeath(sheep);
        }

        // If it was the player who died
        if (sheep == playerSheep)
        {
            playerSheep = null;
        }
    }

    public void PlayerDied()
    {
        // Called from PlayerController when player dies
        // Check if playerSheep is null, if not, set it
        if (playerSheep != null)
        {
            Debug.Log("Player died!");
            SheepDied(playerSheep);
        }
    }

    public void RestartGame()
    {
        // For network games, this should disconnect and return to main menu
        if (isNetworkGame)
        {
            ReturnToMainMenu();
        }
        else
        {
            // Reload the current scene for single player
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }

    public void ReturnToMainMenu()
    {
        // Disconnect if in a network game
        if (isNetworkGame)
        {
            if (NetworkClient.active)
            {
                NetworkManager.singleton.StopClient();
            }
            if (NetworkServer.active)
            {
                NetworkManager.singleton.StopServer();
            }
        }

        // Return to main menu scene
        SceneManager.LoadScene("MainMenu");
    }

    // Public methods for accessing arena information
    public Vector3 GetArenaSize()
    {
        return arenaSize;
    }

    public Vector3 GetArenaCenter()
    {
        // Always use the arena transform's position as the center
        return (arena != null) ? arena.position : Vector3.zero;
    }

    // Spawn networked sheep (for network games)
    public void SpawnNetworkedSheep(int count)
    {
        // Debug point
        Debug.Log($"SpawnNetworkedSheep called with count: {count}");
        
        if (!isNetworkGame)
        {
            Debug.LogError("Attempted to spawn network sheep in non-network game mode");
            return;
        }
        
        if (!NetworkServer.active)
        {
            Debug.LogError("Only the server can spawn networked sheep");
            return;
        }

        Debug.Log($"Spawning {count} networked bots");
        StartCoroutine(SpawnNetworkedSheepProgressively(count));
    }

    // Progressive spawn to avoid network spikes
    private IEnumerator SpawnNetworkedSheepProgressively(int count)
    {
        const int BATCH_SIZE = 5; // Number of bots per batch
        
        Debug.Log($"Starting to spawn {count} bots progressively");
        
        // First ensure player is already in the active sheep list
        NetworkSheepPlayer localPlayer = null;
        if (NetworkClient.localPlayer != null)
        {
            localPlayer = NetworkClient.localPlayer.GetComponent<NetworkSheepPlayer>();
        }
        
        if (localPlayer != null && !activeSheep.Contains(localPlayer.gameObject))
        {
            Debug.Log("Adding local player to active sheep list before spawning bots");
            activeSheep.Add(localPlayer.gameObject);
        }
        
        // Clear or initialize the active sheep list first with players
        List<GameObject> playerSheepList = new List<GameObject>();
        foreach (var sheep in activeSheep)
        {
            if (sheep != null && sheep.GetComponent<NetworkSheepPlayer>() != null)
            {
                playerSheepList.Add(sheep);
            }
        }
        
        // Reset the active sheep list with just the players
        activeSheep.Clear();
        foreach (var playerSheep in playerSheepList)
        {
            activeSheep.Add(playerSheep);
        }
        
        Debug.Log($"Starting with {activeSheep.Count} player sheep before spawning bots");
        
        // Check if the prefab is assigned
        if (aiSheepPrefab == null)
        {
            Debug.LogError("aiSheepPrefab is null! Cannot spawn bots!");
            yield break;
        }
        
        // Now spawn the bots
        for (int i = 0; i < count; i++)
        {
            // Get a random spawn position
            Vector3 spawnPos = GetRandomSpawnPosition(true);
            Quaternion randomRot = Quaternion.Euler(0, Random.Range(0, 360), 0);
            
            // Spawn bot
            GameObject botObject = Instantiate(aiSheepPrefab, spawnPos, randomRot);
            if (botObject == null)
            {
                Debug.LogError("Failed to instantiate bot!");
                continue;
            }
            
            Debug.Log($"Bot {i+1}/{count} instantiated successfully: {botObject.name}");
            
            // Configure network components
            NetworkAISheep networkAI = botObject.GetComponent<NetworkAISheep>();
            if (networkAI != null)
            {
                networkAI.isNetworkBot = true;
            }
            else
            {
                Debug.LogWarning($"Bot {i+1}/{count} is missing NetworkAISheep component");
            }
            
            AIPlayerController aiController = botObject.GetComponent<AIPlayerController>();
            if (aiController != null)
            {
                aiController.isNetworkMode = true;
                
                // Lock movement until game starts
                aiController.SetMovementLocked(!gameStarted);
            }
            else
            {
                Debug.LogWarning($"Bot {i+1}/{count} is missing AIPlayerController component");
            }
            
            // Add to active sheep list
            activeSheep.Add(botObject);
            
            // Spawn on network
            try
            {
                NetworkServer.Spawn(botObject);
                Debug.Log($"Bot {i+1}/{count} successfully spawned on network");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error spawning bot on network: {e.Message}");
            }
            
            // Update UI with each batch to show progress
            if (i % BATCH_SIZE == 0)
            {
                UpdateSheepCountUI();
                Debug.Log($"Progress: Spawned {i+1}/{count} bots, total sheep now: {activeSheep.Count}");
            }
            
            // Spawn in batches to distribute network load
            if (i % BATCH_SIZE == BATCH_SIZE - 1)
            {
                yield return new WaitForSeconds(0.1f);
            }
            else
            {
                // Small delay between individual spawns for stability
                yield return new WaitForSeconds(0.02f);
            }
        }
        
        // Final update with correct count
        Debug.Log($"Final sheep count after spawning: {activeSheep.Count}");
        UpdateSheepCountUI();
        
        // Wait a bit to ensure everything is properly set up
        yield return new WaitForSeconds(0.5f);
        
        // Make sure game is properly started after bots are all ready
        if (!gameStarted)
        {
            Debug.Log("Setting gameStarted to true after bot spawning completed");
            gameStarted = true;
            
            // Unlock all sheep movement if game is starting
            EnableAllSheep();
        }
    }

    // Enhanced random spawn position 
    public Vector3 GetRandomSpawnPosition(bool avoidCenter = false)
    {
        Vector3 centerPos = GetArenaCenter();

        // Arena size with margin
        float arenaWidth = arenaSize.x * 0.8f;  // 80% of arena width to avoid edges
        float arenaLength = arenaSize.z * 0.8f;  // 80% of arena length to avoid edges

        // Determine minimum distance from center based on avoidCenter flag
        float minDistance = avoidCenter ? arenaWidth * 0.2f : 0f;
        float maxDistance = arenaWidth * 0.4f;  // Limit max distance from center to avoid edges

        // Generate random angle and distance for polar coordinates
        float randomAngle = Random.Range(0, 2f * Mathf.PI);
        float randomDistance = Random.Range(minDistance, maxDistance);

        // Convert to cartesian coordinates
        Vector3 spawnPos = new Vector3(
            centerPos.x + randomDistance * Mathf.Cos(randomAngle),
            0,  // Keep on ground plane
            centerPos.z + randomDistance * Mathf.Sin(randomAngle)
        );

        return spawnPos;
    }
}