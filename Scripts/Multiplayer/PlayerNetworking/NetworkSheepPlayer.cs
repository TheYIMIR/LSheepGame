using Mirror;
using UnityEngine;
using System.Collections;

/// <summary>
/// Network component for player-controlled sheep. Handles network synchronization
/// of player state and actions. Inherits from NetworkSheepController base class.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class NetworkSheepPlayer : NetworkSheepController
{
    [Header("Player Info")]
    [SyncVar(hook = nameof(OnPlayerNameChanged))]
    public string playerName = "Sheep";

    [SyncVar]
    public bool hasVoted = false;

    // References to components
    private PlayerController playerController;

    // Time tracking for synchronization
    private float syncInterval = 0.1f; // How often to sync transform (10 times per second)
    private float lastSyncTime = 0;

    // Threshold for sending position updates
    private float positionThreshold = 0.1f;
    private float rotationThreshold = 2.0f;

    // Network smoothing parameters
    [Header("Network Smoothing")]
    [SyncVar]
    private Vector3 serverPosition;

    [SyncVar]
    private Quaternion serverRotation;

    [SyncVar]
    private Vector3 serverVelocity;

    // Last sent values for delta compression
    private Vector3 lastSentPosition;
    private Quaternion lastSentRotation;
    private Vector3 lastSentVelocity;

    protected override void Awake()
    {
        base.Awake();
        playerController = GetComponent<PlayerController>();
    }

    protected override void Start()
    {
        base.Start();

        // If this is local player, set specific tag
        if (isLocalPlayer)
        {
            gameObject.tag = "Player";
        }
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        // Set the player name from player prefs if available
        if (PlayerPrefs.HasKey("PlayerName"))
        {
            CmdSetPlayerName(PlayerPrefs.GetString("PlayerName"));
        }
        else
        {
            CmdSetPlayerName($"Player_{Random.Range(100, 999)}");
        }

        // Find and set up camera to follow this player
        FindAndSetupCamera();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // Initialize network smoothing values
        serverPosition = transform.position;
        serverRotation = transform.rotation;
        serverVelocity = Vector3.zero;

        // Disable player controller on non-local clients
        if (!isLocalPlayer && playerController != null)
        {
            playerController.enabled = false;
        }
    }

    void Update()
    {
        if (isLocalPlayer && !isDead)
        {
            // Check for charging
            if (Input.GetKeyDown(KeyCode.Space) && playerController != null)
            {
                // Notify the server about charging
                CmdStartCharging();
            }

            // Send transform updates periodically
            if (Time.time > lastSyncTime + syncInterval)
            {
                lastSyncTime = Time.time;
                SyncTransformToServer();
            }
        }
        else if (!isLocalPlayer && !isServer)
        {
            // For remote players, update position based on network data
            if (rb != null && serverPosition != Vector3.zero)
            {
                // Let the base SheepController handle the smoothing with our target values
                SetNetworkTarget(serverPosition, serverRotation);
            }
        }
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

    // Called when the player name changes
    void OnPlayerNameChanged(string oldName, string newName)
    {
        Debug.Log($"Player name changed from {oldName} to {newName}");
        // Update UI elements or other game elements that show player name, if needed
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

            // Play the baa sound for non-local players if needed
            AudioSource audioSource = GetComponent<AudioSource>();
            if (audioSource != null && playerController.baaSound != null)
            {
                audioSource.pitch = Random.Range(playerController.minPitch, playerController.maxPitch);
                audioSource.PlayOneShot(playerController.baaSound, playerController.baaVolume);
                audioSource.pitch = 1.0f;
            }
        }
    }

    // Sync transform from client to server
    void SyncTransformToServer()
    {
        if (!rb) return;

        // Only send if position or rotation changed significantly
        Vector3 position = transform.position;
        Quaternion rotation = transform.rotation;
        Vector3 velocity = rb.velocity;

        bool positionChanged = Vector3.Distance(position, lastSentPosition) > positionThreshold;
        bool rotationChanged = Quaternion.Angle(rotation, lastSentRotation) > rotationThreshold;
        bool velocityChanged = Vector3.Distance(velocity, lastSentVelocity) > 1.0f;

        if (positionChanged || rotationChanged || velocityChanged)
        {
            CmdSendTransformToServer(position, rotation, velocity);

            // Update last sent values
            lastSentPosition = position;
            lastSentRotation = rotation;
            lastSentVelocity = velocity;
        }
    }

    // Command to send transform to server
    [Command]
    void CmdSendTransformToServer(Vector3 position, Quaternion rotation, Vector3 velocity)
    {
        // Update server-side values
        serverPosition = position;
        serverRotation = rotation;
        serverVelocity = velocity;

        // Broadcast to other clients
        RpcSyncTransformToClients(position, rotation, velocity);
    }

    // RPC to sync transform to all clients
    [ClientRpc]
    void RpcSyncTransformToClients(Vector3 position, Quaternion rotation, Vector3 velocity)
    {
        // Only apply to remote players
        if (!isLocalPlayer)
        {
            // Update synced values
            serverPosition = position;
            serverRotation = rotation;
            serverVelocity = velocity;

            // For prediction, apply velocity in the Update function
            if (rb != null && rb.isKinematic)
            {
                // If using kinematic rigidbody, directly update position
                SetNetworkTarget(position, rotation);
            }
            // Otherwise, the Update method will handle smoothing with these target values
        }
    }

    // Called when the player dies locally
    public void OnPlayerDeath()
    {
        if (isLocalPlayer && !isDead)
        {
            Debug.Log($"Local player died: {gameObject.name}");
            CmdPlayerDied();
        }
    }

    // Command to notify server of player death
    [Command]
    void CmdPlayerDied()
    {
        // Avoid duplicate deaths
        if (isDead) return;

        // Use the base class method to sync death
        SyncDeath();
    }

    // Override RPC from base class for type-specific handling
    public override void RpcApplyHitForce(Vector3 direction, float force)
    {
        if (isServer) return; // Server already applied effects

        // Apply to the player controller
        if (playerController != null)
        {
            playerController.ApplyNetworkHitForce(direction, force);
        }
    }

    // Override death handling for player-specific behavior
    protected override void HandleDeath()
    {
        if (isDead) return; // Prevent multiple death processes

        base.HandleDeath();

        // Player-specific death handling
        if (isLocalPlayer)
        {
            // Show defeat UI or other local player death effects
            GameManager gameManager = GameManager.Instance;
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
    }
}