using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

/// <summary>
/// Network component for AI-controlled sheep. Handles network synchronization
/// of AI state and behavior. Inherits from NetworkSheepController base class.
/// </summary>
[RequireComponent(typeof(AIPlayerController))]
public class NetworkAISheep : NetworkSheepController
{
    // Reference to the AI controller
    private AIPlayerController aiController;

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

    protected override void Awake()
    {
        base.Awake();
        aiController = GetComponent<AIPlayerController>();
    }

    protected override void Start()
    {
        base.Start();

        // Set initial AI state
        if (isServer)
        {
            currentStateInt = (int)AIPlayerController.AIState.Wander;
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

    // Override RPC from base class for type-specific handling
    public override void RpcApplyHitForce(Vector3 direction, float force)
    {
        if (isServer) return; // Server already applied effects

        // Apply to the AI controller
        if (aiController != null)
        {
            aiController.ApplyNetworkHitForce(direction, force);
        }
    }

    // Override death handling for AI-specific behavior
    protected override void HandleDeath()
    {
        if (isDead) return; // Prevent multiple death processes

        base.HandleDeath();

        // AI-specific death handling can go here
        Debug.Log($"AI sheep death: {gameObject.name}");
    }
}