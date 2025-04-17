using Mirror;
using UnityEngine;

[RequireComponent(typeof(PlayerController))]
public class NetworkSheepPlayer : NetworkBehaviour
{
    [Header("Network Synced Variables")]
    [SyncVar(hook = nameof(OnNameChanged))]
    public string playerName = "NetworkSheep";

    [SyncVar]
    public bool hasVoted = false;

    [SyncVar(hook = nameof(OnDeadStatusChanged))]
    public bool isDead = false;

    // Reference to the base player controller
    private PlayerController playerController;
    private Rigidbody rb;

    // Cache game manager reference
    private GameManager gameManager;

    void Awake()
    {
        playerController = GetComponent<PlayerController>();
        rb = GetComponent<Rigidbody>();

        // Set tag for both client and server
        gameObject.tag = "Sheep";
    }

    void Start()
    {
        // Cache game manager reference
        gameManager = GameManager.Instance;

        // Make sure we're added to active sheep list
        if (gameManager != null && !gameManager.activeSheep.Contains(gameObject))
        {
            gameManager.activeSheep.Add(gameObject);
            Debug.Log($"Added player to active list: {gameObject.name}");
        }
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        // If no name was set, generate a random one
        if (string.IsNullOrEmpty(playerName))
        {
            playerName = $"Sheep_{Random.Range(100, 999)}";
        }

        // We need to add ourselves to the GameManager's active sheep list
        StartCoroutine(DelayedAddToActiveSheep());
    }

    private System.Collections.IEnumerator DelayedAddToActiveSheep()
    {
        yield return new WaitForSeconds(0.2f);

        if (gameManager == null)
        {
            gameManager = GameManager.Instance;
        }

        if (gameManager != null && !gameManager.activeSheep.Contains(gameObject))
        {
            gameManager.activeSheep.Add(gameObject);
            Debug.Log($"Added player to active list (delayed): {gameObject.name}");
        }
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // Make sure PlayerController knows we're in network mode
        if (playerController != null)
        {
            playerController.isNetworkMode = true;
        }

        // Disable player controller on non-local clients
        if (!isLocalPlayer && playerController != null)
        {
            playerController.enabled = false;
        }

        // Set player movement to locked initially (will be unlocked when game starts)
        if (playerController != null)
        {
            playerController.SetMovementLocked(true);
        }
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        // Load the player name from player prefs if available
        if (PlayerPrefs.HasKey("PlayerName"))
        {
            CmdSetPlayerName(PlayerPrefs.GetString("PlayerName"));
        }
        else
        {
            CmdSetPlayerName($"Player_{Random.Range(100, 999)}");
        }

        // Set the tag for local player
        gameObject.tag = "Player";

        // Tell the camera to follow this player
        FindAndSetupCamera();
    }

    // Find camera and set it to follow this player
    void FindAndSetupCamera()
    {
        SmoothCameraFollow cameraFollow = FindObjectOfType<SmoothCameraFollow>();
        if (cameraFollow != null)
        {
            cameraFollow.target = transform;
            Debug.Log("Camera now following local player");
        }
    }

    // Command to set the player's name on the server
    [Command]
    public void CmdSetPlayerName(string newName)
    {
        // Simple validation
        if (string.IsNullOrEmpty(newName) || newName.Length > 20)
            return;

        playerName = newName;
    }

    // Command to register a vote to start
    [Command]
    public void CmdVote()
    {
        if (!hasVoted)
        {
            hasVoted = true;

            // Notify the lobby manager of the vote
            NetworkLobbyManager lobbyManager = FindObjectOfType<NetworkLobbyManager>();
            if (lobbyManager != null)
            {
                lobbyManager.PlayerVoted();
            }
        }
    }

    // Command to start charging
    [Command]
    public void CmdStartCharging()
    {
        // Tell all clients that this player is charging
        RpcOnStartCharging();
    }

    // ClientRpc to sync charging across all clients
    [ClientRpc]
    void RpcOnStartCharging()
    {
        // Don't play sound again for local player (they already did)
        if (isLocalPlayer)
            return;

        // Make non-local players charge
        if (playerController != null)
        {
            playerController.NetworkCharge();

            // Play the baa sound for non-local players
            AudioSource audioSource = GetComponent<AudioSource>();
            if (audioSource != null && playerController.baaSound != null)
            {
                audioSource.pitch = Random.Range(playerController.minPitch, playerController.maxPitch);
                audioSource.PlayOneShot(playerController.baaSound, playerController.baaVolume);
                audioSource.pitch = 1.0f;
            }
        }
    }

    // Called when the player name changes
    void OnNameChanged(string oldName, string newName)
    {
        // Update name display or any UI elements
        Debug.Log($"Player name changed from {oldName} to {newName}");
    }

    // Called when death status changes
    void OnDeadStatusChanged(bool oldValue, bool newValue)
    {
        if (newValue && !oldValue)
        {
            // Process death on all clients
            Debug.Log($"Player death status changed to dead: {gameObject.name}");
            ProcessDeath();
        }
    }

    // Handle collisions between network players
    public void HandleHitNetworkPlayer(uint targetNetId, float impactForce)
    {
        if (isLocalPlayer)
        {
            CmdHitPlayer(targetNetId, transform.position, impactForce);
        }
    }

    [Command]
    void CmdHitPlayer(uint targetNetId, Vector3 hitPosition, float impactForce)
    {
        if (!NetworkServer.spawned.ContainsKey(targetNetId))
            return;

        // Get the target NetworkIdentity
        NetworkIdentity targetIdentity = NetworkServer.spawned[targetNetId];
        if (targetIdentity == null)
            return;

        // Get the target's position
        GameObject targetObj = targetIdentity.gameObject;
        Vector3 targetPos = targetObj.transform.position;

        // Calculate hit direction
        Vector3 hitDirection = (targetPos - hitPosition).normalized;
        hitDirection.y = 0; // Keep on horizontal plane

        // Check if target is an AI or player
        NetworkSheepPlayer targetPlayer = targetObj.GetComponent<NetworkSheepPlayer>();
        if (targetPlayer != null)
        {
            targetPlayer.RpcApplyHitForce(hitDirection, impactForce);
        }
        else
        {
            NetworkAISheep targetAI = targetObj.GetComponent<NetworkAISheep>();
            if (targetAI != null)
            {
                targetAI.RpcApplyHitForce(hitDirection, impactForce);
            }
        }
    }

    [ClientRpc]
    public void RpcApplyHitForce(Vector3 direction, float force)
    {
        if (playerController != null)
        {
            playerController.ApplyNetworkHitForce(direction, force);
        }
    }

    // Called when the player dies, need to send to server
    public void OnPlayerDeath()
    {
        if (isLocalPlayer && !isDead)
        {
            Debug.Log($"Local player died: {gameObject.name}");
            CmdPlayerDied();
        }
    }

    [Command]
    void CmdPlayerDied()
    {
        // Avoid duplicate deaths
        if (isDead) return;

        Debug.Log($"Player death command received on server: {gameObject.name}");
        isDead = true;

        // Notify game manager on server
        if (gameManager != null)
        {
            Debug.Log($"Notifying game manager of player death: {gameObject.name}");
            gameManager.SheepDied(gameObject);
        }
        else
        {
            Debug.LogError("Game manager is null when trying to notify of player death");
        }
    }

    // Process death effects (called on all clients)
    void ProcessDeath()
    {
        Debug.Log($"Processing player death: {gameObject.name}");

        // Update local UI to show this player is dead
        MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>();
        foreach (MeshRenderer renderer in renderers)
        {
            renderer.enabled = false;
        }

        // Disable physics interactions
        if (rb != null)
        {
            rb.isKinematic = true;
        }

        // Play death effects if this is the local player
        if (isLocalPlayer && playerController != null)
        {
            // Play explosion effect without triggering more network messages
            playerController.PlayDeathEffects();

            // Show defeat UI
            if (gameManager != null && gameManager.defeatPanel != null)
            {
                gameManager.defeatPanel.SetActive(true);

                // Show main menu button
                if (gameManager.mainMenuButton != null)
                {
                    gameManager.mainMenuButton.gameObject.SetActive(true);
                }
            }
        }

        // If not server, notify game manager directly on client
        if (!isServer && gameManager != null && gameManager.activeSheep.Contains(gameObject))
        {
            gameManager.SheepDied(gameObject);
        }
    }
}