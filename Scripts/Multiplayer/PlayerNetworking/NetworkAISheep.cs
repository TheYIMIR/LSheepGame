using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

// This component handles the network-specific functionality for AI sheep
[RequireComponent(typeof(AIPlayerController))]
public class NetworkAISheep : NetworkBehaviour
{
    // Reference to the base AI controller
    private AIPlayerController aiController;
    private Rigidbody rb;

    // Flag to identify network-spawned bots
    [SyncVar]
    public bool isNetworkBot = false;

    // Synced state for AI
    [SyncVar(hook = nameof(OnStateChanged))]
    private int currentStateInt = 0;

    [SyncVar]
    private float stateTimer = 0f;

    // Synced target position
    [SyncVar(hook = nameof(OnTargetPositionChanged))]
    private Vector3 syncedTargetPosition;

    // Synced death status
    [SyncVar(hook = nameof(OnDeathStatusChanged))]
    private bool isDead = false;

    // Cache reference to game manager
    private GameManager gameManager;

    void Awake()
    {
        aiController = GetComponent<AIPlayerController>();
        rb = GetComponent<Rigidbody>();

        // Set tag for both client and server
        gameObject.tag = "Sheep";
    }

    void Start()
    {
        // Make sure AIPlayerController knows we're in network mode
        if (aiController != null)
        {
            aiController.isNetworkMode = true;
        }

        // Cache reference to game manager
        gameManager = GameManager.Instance;

        // Make sure we're added to active sheep list on server
        if (isServer && gameManager != null && !gameManager.activeSheep.Contains(gameObject))
        {
            gameManager.activeSheep.Add(gameObject);
            Debug.Log($"Added AI sheep to active list: {gameObject.name}");
        }

        // Initially lock AI movement until game starts
        if (aiController != null)
        {
            aiController.SetMovementLocked(true);

            // Unlock if game already started
            if (gameManager != null && gameManager.gameStarted)
            {
                aiController.SetMovementLocked(false);
            }
        }
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        // Initialize AI state for server
        currentStateInt = (int)AIPlayerController.AIState.Wander;
        stateTimer = 0;

        // We need to add ourselves to the GameManager's active sheep list
        StartCoroutine(DelayedAddToActiveSheep());
    }

    private IEnumerator DelayedAddToActiveSheep()
    {
        yield return new WaitForSeconds(0.2f);

        if (gameManager == null)
        {
            gameManager = GameManager.Instance;
        }

        if (gameManager != null && !gameManager.activeSheep.Contains(gameObject))
        {
            gameManager.activeSheep.Add(gameObject);
            Debug.Log($"Added AI sheep to active list (delayed): {gameObject.name}");
        }
    }

    // Sync AI state from server to clients
    public void SyncAIState(int stateInt, float timer)
    {
        if (!isServer) return;

        currentStateInt = stateInt;
        stateTimer = timer;
    }

    // Called when state changes (on clients)
    void OnStateChanged(int oldState, int newState)
    {
        if (isServer) return; // Server already has correct state

        // Update AI controller with new state
        if (aiController != null)
        {
            aiController.SetAIState(newState, stateTimer);
        }
    }

    // Sync target position for wander/flee states
    public void SyncTargetPosition(Vector3 position)
    {
        if (!isServer) return;

        syncedTargetPosition = position;
    }

    // Called when target position changes (on clients)
    void OnTargetPositionChanged(Vector3 oldPos, Vector3 newPos)
    {
        if (isServer) return; // Server already has correct position

        // Update AI controller with new target position
        if (aiController != null)
        {
            aiController.SetTargetPosition(newPos);
        }
    }

    // Sync charge action across network
    public void SyncChargeAction()
    {
        if (!isServer) return;

        // Tell all clients this AI is charging
        RpcDoCharge();
    }

    // RPC to trigger charge effect on all clients
    [ClientRpc]
    void RpcDoCharge()
    {
        // Don't re-apply on server
        if (isServer) return;

        // Execute the charge on client
        if (aiController != null)
        {
            aiController.ExecuteCharge();
        }
    }

    // Sync hit to network player or AI
    public void SyncHitNetworkEntity(uint targetNetId, Vector3 direction, float force)
    {
        if (!isServer) return;

        // Determine if target is player or AI and call appropriate method
        if (NetworkServer.spawned.TryGetValue(targetNetId, out NetworkIdentity targetIdentity))
        {
            if (targetIdentity.GetComponent<NetworkSheepPlayer>() != null)
            {
                RpcHitPlayer(targetNetId, direction, force);
            }
            else if (targetIdentity.GetComponent<NetworkAISheep>() != null)
            {
                RpcHitAI(targetNetId, direction, force);
            }
        }
    }

    // RPC to apply hit effects to player
    [ClientRpc]
    void RpcHitPlayer(uint targetNetId, Vector3 direction, float force)
    {
        if (isServer) return; // Server already applied effects

        // Find the target by netId
        if (NetworkClient.spawned.TryGetValue(targetNetId, out NetworkIdentity targetIdentity))
        {
            NetworkSheepPlayer player = targetIdentity.GetComponent<NetworkSheepPlayer>();
            if (player != null)
            {
                // Apply hit forces on the client side
                PlayerController playerController = player.GetComponent<PlayerController>();
                if (playerController != null)
                {
                    playerController.ApplyNetworkHitForce(direction, force);
                }
            }
        }
    }

    // RPC to apply hit effects to AI
    [ClientRpc]
    void RpcHitAI(uint targetNetId, Vector3 direction, float force)
    {
        if (isServer) return; // Server already applied effects

        // Find the target by netId
        if (NetworkClient.spawned.TryGetValue(targetNetId, out NetworkIdentity targetIdentity))
        {
            NetworkAISheep targetAI = targetIdentity.GetComponent<NetworkAISheep>();
            if (targetAI != null)
            {
                // Apply hit forces on the client side
                AIPlayerController aiController = targetAI.GetComponent<AIPlayerController>();
                if (aiController != null)
                {
                    aiController.ApplyNetworkHitForce(direction, force);
                }
            }
        }
    }

    // Method for clients to apply network hit force
    [ClientRpc]
    public void RpcApplyHitForce(Vector3 direction, float force)
    {
        if (aiController != null)
        {
            aiController.ApplyNetworkHitForce(direction, force);
        }
    }

    // Sync death across network
    public void SyncDeath()
    {
        if (!isServer) return;

        // Log the death
        Debug.Log($"AI sheep death synced: {gameObject.name}");

        // Set the death status
        isDead = true;

        // Notify all clients
        RpcOnDeath();

        // Notify game manager
        if (gameManager != null)
        {
            gameManager.SheepDied(gameObject);
        }
    }

    // Called when death status changes (on clients)
    void OnDeathStatusChanged(bool oldValue, bool newValue)
    {
        if (newValue && !oldValue)
        {
            // If we went from alive to dead
            Debug.Log($"AI sheep death status changed to dead: {gameObject.name}");
            HandleDeath();
        }
    }

    // RPC to notify all clients about death
    [ClientRpc]
    void RpcOnDeath()
    {
        if (isServer) return; // Server already handled death

        Debug.Log($"RPC received for AI sheep death: {gameObject.name}");
        HandleDeath();
    }

    // Handles death effects on all clients
    void HandleDeath()
    {
        if (isDead) return; // Prevent multiple death processes

        Debug.Log($"Handling AI sheep death: {gameObject.name}");

        if (aiController != null)
        {
            // Call Die() on the controller, but don't let it destroy the object (server will do that)
            aiController.Die();
        }

        // Disable mesh renderer
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

        // If not server, notify game manager directly on client
        if (!isServer && gameManager != null && gameManager.activeSheep.Contains(gameObject))
        {
            gameManager.SheepDied(gameObject);
        }
    }

    // This method can be called to ensure server authority over AI sheep
    [Server]
    public void ServerSetLocked(bool locked)
    {
        if (aiController != null)
        {
            aiController.SetMovementLocked(locked);
        }

        // Sync to clients
        RpcSetLocked(locked);
    }

    [ClientRpc]
    void RpcSetLocked(bool locked)
    {
        if (isServer) return; // Server already set this

        if (aiController != null)
        {
            aiController.SetMovementLocked(locked);
        }
    }
}