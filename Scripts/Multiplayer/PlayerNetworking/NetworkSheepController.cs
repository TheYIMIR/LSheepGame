using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

/// <summary>
/// Base network class for all sheep network components.
/// Handles common network functionality for both AI and player sheep.
/// </summary>
[RequireComponent(typeof(SheepController))]
public abstract class NetworkSheepController : NetworkBehaviour
{
    [Header("Base Network Properties")]
    [SyncVar(hook = nameof(OnDeadStatusChanged))]
    public bool isDead = false;

    [Header("Network Smoothing")]
    public float positionSmoothTime = 0.1f;
    public float rotationSmoothTime = 0.1f;
    public float maxSmoothingSpeed = 10f;
    public float networkSmoothingThreshold = 0.15f;

    // Reference to the base sheep controller component
    protected SheepController sheepController;
    protected Rigidbody rb;
    protected GameManager gameManager;

    // Network movement variables
    protected bool movementLocked = false;
    protected Vector3 targetPosition;
    protected Quaternion targetRotation;

    // Network smoothing variables
    protected Vector3 smoothVelocity;
    protected float lastNetworkUpdateTime;

    protected virtual void Awake()
    {
        // Get component references
        sheepController = GetComponent<SheepController>();
        rb = GetComponent<Rigidbody>();

        // Set tag for both client and server
        gameObject.tag = "Sheep";
    }

    protected virtual void Start()
    {
        // Cache reference to game manager
        gameManager = GameManager.Instance;

        // Make sure we're added to active sheep list
        if (gameManager != null && !gameManager.activeSheep.Contains(gameObject))
        {
            gameManager.activeSheep.Add(gameObject);
            Debug.Log($"Added sheep to active list: {gameObject.name}");
        }

        // Ensure sheep is set up for network mode
        if (sheepController != null)
        {
            sheepController.isNetworkMode = true;
        }

        // Initially lock movement until game starts
        if (sheepController != null)
        {
            sheepController.SetMovementLocked(true);
            movementLocked = true;
        }

        // Initialize network smoothing
        lastNetworkUpdateTime = Time.time;
        targetPosition = transform.position;
        targetRotation = transform.rotation;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        // Add to game manager's active sheep list on server
        StartCoroutine(DelayedAddToActiveSheep());
    }

    // Make sure the sheep is added to the active sheep list
    protected IEnumerator DelayedAddToActiveSheep()
    {
        yield return new WaitForSeconds(0.2f);

        if (gameManager == null)
        {
            gameManager = GameManager.Instance;
        }

        if (gameManager != null && !gameManager.activeSheep.Contains(gameObject))
        {
            gameManager.activeSheep.Add(gameObject);
            Debug.Log($"Added to active list (delayed): {gameObject.name}");
        }
    }

    // Set the target position and rotation for network smoothing
    public virtual void SetNetworkTarget(Vector3 position, Quaternion rotation)
    {
        // Update the smoothing targets
        targetPosition = position;
        targetRotation = rotation;

        // Pass these to the sheep controller for smoothing if it supports it
        if (sheepController != null)
        {
            sheepController.SetNetworkTarget(position, rotation);
        }
    }

    // Method for applying network hit force to this entity
    [ClientRpc]
    public virtual void RpcApplyHitForce(Vector3 direction, float force)
    {
        if (isServer) return; // Server already applied this

        if (sheepController != null)
        {
            sheepController.ApplyNetworkHitForce(direction, force);
        }
    }

    // Method to sync hit from this entity to another entity
    public virtual void SyncHitNetworkEntity(uint targetNetId, Vector3 direction, float force)
    {
        if (!isServer) return;

        // Find the target identity
        if (NetworkServer.spawned.TryGetValue(targetNetId, out NetworkIdentity targetIdentity))
        {
            // Check for any type of NetworkSheepController
            NetworkSheepController targetController = targetIdentity.GetComponent<NetworkSheepController>();
            if (targetController != null)
            {
                // Apply hit force directly to the target using our common base class
                targetController.RpcApplyHitForce(direction, force);
            }
        }
    }

    // Sync death across network
    public virtual void SyncDeath()
    {
        if (!isServer) return;

        // Set death status and notify clients
        isDead = true;
        RpcOnDeath();

        // Notify game manager
        if (gameManager != null)
        {
            gameManager.SheepDied(gameObject);
        }
    }

    // Called when death status changes (on clients)
    protected virtual void OnDeadStatusChanged(bool oldValue, bool newValue)
    {
        if (newValue && !oldValue)
        {
            // If we went from alive to dead
            Debug.Log($"Death status changed to dead: {gameObject.name}");
            HandleDeath();
        }
    }

    // RPC to notify all clients about death
    [ClientRpc]
    protected virtual void RpcOnDeath()
    {
        if (isServer) return; // Server already handled death

        Debug.Log($"RPC received for death: {gameObject.name}");
        HandleDeath();
    }

    // Handles death effects on all clients
    protected virtual void HandleDeath()
    {
        if (isDead) return; // Prevent multiple death processes

        Debug.Log($"Handling death: {gameObject.name}");

        // Call Die() on the SheepController - this should always be available
        // since NetworkSheepController requires SheepController
        if (sheepController != null)
        {
            sheepController.Die();
        }
        else
        {
            // This should never happen, but log an error if it somehow does
            Debug.LogError($"SheepController is null on {gameObject.name} during death handling!");
        }

        // If not server, notify game manager directly on client
        if (!isServer && gameManager != null && gameManager.activeSheep.Contains(gameObject))
        {
            gameManager.SheepDied(gameObject);
        }
    }

    // Method to lock/unlock movement
    [ClientRpc]
    public virtual void RpcSetLocked(bool locked)
    {
        if (isServer) return; // Server already set this

        movementLocked = locked;

        if (sheepController != null)
        {
            sheepController.SetMovementLocked(locked);
        }
    }

    // Server method to set locked state and sync to clients
    [Server]
    public virtual void ServerSetLocked(bool locked)
    {
        movementLocked = locked;

        if (sheepController != null)
        {
            sheepController.SetMovementLocked(locked);
        }

        // Sync to clients
        RpcSetLocked(locked);
    }
}