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

    // Game state - made public for network access
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
        StartCoroutine(InitializeGame());
    }

    IEnumerator InitializeGame()
    {
        // Show countdown
        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(true);
        }

        if (isNetworkGame)
        {
            // In network mode, wait for all players to be ready
            if (NetworkServer.active)
            {
                yield return WaitForNetworkPlayers();
            }
            else
            {
                // Wait for a bit to let the server set things up
                yield return new WaitForSeconds(1f);
            }

            // Find network players and add them to active sheep
            CollectNetworkPlayers();
        }
        else
        {
            // Spawn local player first
            SpawnLocalPlayer();

            // Spawn AI sheep
            for (int i = 0; i < initialSheepCount - 1; i++) // -1 because player is already spawned
            {
                SpawnAISheep();
                yield return new WaitForSeconds(0.1f); // Small delay between spawns for performance
            }
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

    // Helper method to wait for network players
    IEnumerator WaitForNetworkPlayers()
    {
        // Give clients time to join and spawn
        float waitTime = 2f;
        while (waitTime > 0)
        {
            waitTime -= Time.deltaTime;
            yield return null;
        }

        // If we're the server, spawn bots to fill remaining slots
        if (NetworkServer.active)
        {
            int playerCount = 0;

            // Count all network player objects
            NetworkSheepPlayer[] networkPlayers = FindObjectsOfType<NetworkSheepPlayer>();
            playerCount = networkPlayers.Length;

            // Spawn bots for the remaining slots
            int botsNeeded = initialSheepCount - playerCount;
            for (int i = 0; i < botsNeeded; i++)
            {
                SpawnAISheep();
                yield return new WaitForSeconds(0.1f);
            }
        }
    }

    // Helper method to collect network players
    void CollectNetworkPlayers()
    {
        // Find all NetworkSheepPlayer objects
        NetworkSheepPlayer[] networkPlayers = FindObjectsOfType<NetworkSheepPlayer>();

        foreach (NetworkSheepPlayer netPlayer in networkPlayers)
        {
            GameObject player = netPlayer.gameObject;

            // Add to active sheep list
            if (!activeSheep.Contains(player))
            {
                activeSheep.Add(player);
            }

            // Set local player reference if this is our player
            if (netPlayer.isLocalPlayer)
            {
                playerSheep = player;
                Debug.Log("Set local player reference: " + player.name);
            }
        }

        Debug.Log($"Collected {networkPlayers.Length} network players");
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

        // Spawn player at center
        playerSheep = Instantiate(playerSheepPrefab, arenaCenter, Quaternion.identity);

        // Add to active sheep list
        activeSheep.Add(playerSheep);

        // Initially disable player control
        PlayerController playerController = playerSheep.GetComponent<PlayerController>();
        if (playerController != null)
        {
            playerController.enabled = false;
        }
    }

    void SpawnAISheep()
    {
        // Get a random position within a radius from the center
        Vector3 centerPos = GetArenaCenter();

        // Calculate random spawn position (in a circle)
        float spawnRadius = Mathf.Min(arenaSize.x, arenaSize.z) * 0.4f;
        float randomAngle = Random.Range(0, 2f * Mathf.PI);
        float randomDistance = Random.Range(spawnRadius * 0.5f, spawnRadius);

        Vector3 spawnPos = new Vector3(
            centerPos.x + randomDistance * Mathf.Cos(randomAngle),
            0,
            centerPos.z + randomDistance * Mathf.Sin(randomAngle)
        );

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
            }

            AIPlayerController aiController = sheep.GetComponent<AIPlayerController>();
            if (aiController != null)
            {
                aiController.enabled = true;
            }

            // For network sheep, enable their controllers too
            NetworkSheepPlayer netPlayer = sheep.GetComponent<NetworkSheepPlayer>();
            if (netPlayer != null)
            {
                // Enable the player's controller components
                PlayerController netPlayerController = sheep.GetComponent<PlayerController>();
                if (netPlayerController != null)
                {
                    netPlayerController.enabled = true;
                }
            }
        }
    }

    void Update()
    {
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

        // Game end conditions - handle differently for network games
        if (isNetworkGame)
        {
            // For network games, the NetworkGameManager handles win/loss conditions
            if (!playerAlive && !gameOver)
            {
                // Player died, show defeat panel for this player only
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
        }
        else
        {
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
            // Debug.Log("Applying boundaries to host player");
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

    // Helper method for spawning networked sheep from network manager
    public void SpawnNetworkedSheep(int count)
    {
        if (!isNetworkGame || !NetworkServer.active) return;

        for (int i = 0; i < count; i++)
        {
            SpawnAISheep();
        }
    }

    // Find a random spawn position for a player (for network spawning)
    public Vector3 GetRandomSpawnPosition()
    {
        Vector3 centerPos = GetArenaCenter();

        // Use a smaller radius to keep players from spawning too far out
        float spawnRadius = Mathf.Min(arenaSize.x, arenaSize.z) * 0.3f;
        float randomAngle = Random.Range(0, 2f * Mathf.PI);
        float randomDistance = Random.Range(spawnRadius * 0.3f, spawnRadius);

        Vector3 spawnPos = new Vector3(
            centerPos.x + randomDistance * Mathf.Cos(randomAngle),
            0,
            centerPos.z + randomDistance * Mathf.Sin(randomAngle)
        );

        return spawnPos;
    }
}