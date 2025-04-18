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

    // Client-synced transform
    [SyncVar]
    private Vector3 serverPosition;

    [SyncVar]
    private Quaternion serverRotation;

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

    // Command to register a vote
    [Command]
    public void CmdVote()
    {
        if (!hasVoted)
        {
            hasVoted = true;

            // Inform any vote tracking system if needed
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
        // Only send if position or rotation changed significantly
        Vector3 position = transform.position;
        Quaternion rotation = transform.rotation;

        if (Vector3.Distance(position, serverPosition) > positionThreshold ||
            Quaternion.Angle(rotation, serverRotation) > rotationThreshold)
        {
            CmdSendTransformToServer(position, rotation);
        }
    }

    // Command to send transform to server
    [Command]
    void CmdSendTransformToServer(Vector3 position, Quaternion rotation)
    {
        // Update server-side values
        serverPosition = position;
        serverRotation = rotation;

        // Broadcast to other clients
        RpcSyncTransformToClients(position, rotation);
    }

    // RPC to sync transform to all clients
    [ClientRpc]
    void RpcSyncTransformToClients(Vector3 position, Quaternion rotation)
    {
        // Only apply to remote players
        if (!isLocalPlayer)
        {
            transform.position = position;
            transform.rotation = rotation;
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