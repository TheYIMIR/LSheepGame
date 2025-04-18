using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

/// <summary>
/// AI controller for sheep in the game.
/// Handles AI behavior and decision making while inheriting physics from SheepController.
/// </summary>
public class AIPlayerController : SheepController
{
    [Header("AI Behavior Settings")]
    public float detectionRadius = 6f;
    public float minFleeDistance = 3f;
    public float maxFleeDistance = 8f;
    public float personalSpaceRadius = 2f; // Distance to keep from other sheep
    public float wanderRadius = 6f; // How far to wander from current position
    public float fleeChance = 0.3f; // Chance to flee instead of attack after spotting a target
    public float behaviorChangeInterval = 3f; // How often the AI can change its mind
    public float chargeDistance = 4f; // Distance at which to initiate a charge
    public float chargeTime = 0.3f; // Brief charge-up time
    public LayerMask sheepLayerMask;

    // Network component reference
    private NetworkAISheep networkAI;

    // AI State variables
    public enum AIState { Wander, Chase, Charge, Flee, Recover }
    private AIState currentState = AIState.Wander;
    private Vector3 targetPosition;
    private Transform targetSheep;
    private float stateTimer = 0f;
    private float behaviorTimer = 0f;
    private bool hasSucessfullyHit = false;
    private bool canCharge = true;

    protected override void Awake()
    {
        base.Awake();

        // Try to get network component if it exists
        networkAI = GetComponent<NetworkAISheep>();
    }

    protected override void Start()
    {
        base.Start();

        // Set initial wander target
        SetNewWanderTarget();

        // Initialize behavior timer with random offset to desynchronize AI behavior
        behaviorTimer = Random.Range(0f, behaviorChangeInterval);
    }

    void Update()
    {
        if (isDead || movementLocked) return;

        // In network mode, only the server controls AI behavior
        if (isNetworkMode && !NetworkServer.active)
        {
            return;
        }

        // Update behavior timer
        behaviorTimer -= Time.deltaTime;
        if (behaviorTimer <= 0)
        {
            // Time to potentially change behavior
            EvaluateBehavior();
            behaviorTimer = behaviorChangeInterval;
        }

        // Update state timer
        stateTimer -= Time.deltaTime;
    }

    protected override bool ShouldHandlePhysics()
    {
        // For network mode, only server controls AI physics
        if (isNetworkMode && !NetworkServer.active)
        {
            return false;
        }
        return true;
    }

    protected override bool ShouldApplyStrongStabilization()
    {
        // Don't apply strong stabilization during charge
        return currentState != AIState.Charge;
    }

    protected override void PerformStatePhysicsUpdate(float currentAngle)
    {
        // Switch to recovery state if significantly tilted but not dead
        if (currentAngle > 50f && currentState != AIState.Recover)
        {
            currentState = AIState.Recover;
            stateTimer = 2f; // Recover for 2 seconds

            // Sync state in network mode
            if (isNetworkMode && networkAI != null)
            {
                networkAI.SyncAIState((int)currentState, stateTimer);
            }
        }

        // Execute current state behavior
        switch (currentState)
        {
            case AIState.Wander:
                Wander();
                LookForTarget();
                break;

            case AIState.Chase:
                ChaseTarget();
                break;

            case AIState.Charge:
                if (stateTimer <= 0)
                {
                    // Perform the charge
                    Charge();

                    // After charging, decide what to do next
                    if (hasSucessfullyHit || Random.value < 0.7f)
                    {
                        // Either flee or find a new target
                        if (Random.value < 0.6f)
                        {
                            SetFleeState();
                        }
                        else
                        {
                            ChangeState(AIState.Wander);
                            behaviorTimer = behaviorChangeInterval * 0.5f;
                        }
                    }
                    else
                    {
                        // Didn't hit, try again or wander
                        if (Random.value < 0.3f && targetSheep != null)
                        {
                            ChangeState(AIState.Chase);
                        }
                        else
                        {
                            ChangeState(AIState.Wander);
                            behaviorTimer = behaviorChangeInterval * 0.5f;
                        }
                    }
                }
                break;

            case AIState.Flee:
                Flee();
                break;

            case AIState.Recover:
                ApplyRecoveryForces();
                // Once timer expires and somewhat upright, go back to wandering
                if (stateTimer <= 0 && currentAngle < 30f)
                {
                    ChangeState(AIState.Wander);
                }
                break;
        }
    }

    // Helper method to change state and sync in network mode
    void ChangeState(AIState newState)
    {
        currentState = newState;

        // Sync state in network mode
        if (isNetworkMode && networkAI != null && NetworkServer.active)
        {
            networkAI.SyncAIState((int)currentState, stateTimer);
        }
    }

    // Method to directly set AI state (called from NetworkAISheep)
    public void SetAIState(int stateInt, float timer)
    {
        currentState = (AIState)stateInt;
        stateTimer = timer;
    }

    void EvaluateBehavior()
    {
        // Don't change behavior if recovering
        if (currentState == AIState.Recover)
            return;

        // Find nearby sheep to make informed decisions
        Collider[] nearbySheep = Physics.OverlapSphere(transform.position, detectionRadius, sheepLayerMask);

        if (nearbySheep.Length > 0)
        {
            // There are sheep nearby
            if (currentState == AIState.Wander)
            {
                // Decide whether to chase or flee based on personality and situation
                if (Random.value < fleeChance)
                {
                    SetFleeState();
                }
                else
                {
                    // Find nearest sheep and chase it
                    Transform closestSheep = FindClosestSheep(nearbySheep);
                    if (closestSheep != null)
                    {
                        targetSheep = closestSheep;
                        ChangeState(AIState.Chase);
                    }
                }
            }
            else if (currentState == AIState.Flee)
            {
                // Sometimes stop fleeing and go on the attack
                if (Random.value < 0.3f)
                {
                    Transform closestSheep = FindClosestSheep(nearbySheep);
                    if (closestSheep != null)
                    {
                        targetSheep = closestSheep;
                        ChangeState(AIState.Chase);
                    }
                }
            }
        }
        else
        {
            // No sheep nearby, go back to wandering
            if (currentState != AIState.Wander)
            {
                ChangeState(AIState.Wander);
                SetNewWanderTarget();
            }
        }
    }

    Transform FindClosestSheep(Collider[] nearbySheep)
    {
        Transform closest = null;
        float closestDistance = float.MaxValue;

        foreach (Collider col in nearbySheep)
        {
            // Don't target self
            if (col.transform == transform)
                continue;

            // Skip inactive sheep
            if (!col.gameObject.activeSelf)
                continue;

            // Skip dead sheep
            PlayerController playerController = col.GetComponent<PlayerController>();
            if (playerController != null && playerController.enabled == false)
                continue;

            AIPlayerController aiController = col.GetComponent<AIPlayerController>();
            if (aiController != null && aiController.enabled == false)
                continue;

            float distance = Vector3.Distance(transform.position, col.transform.position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = col.transform;
            }
        }

        return closest;
    }

    void SetFleeState()
    {
        ChangeState(AIState.Flee);

        // Find a flee direction away from nearby sheep
        Collider[] nearbySheep = Physics.OverlapSphere(transform.position, detectionRadius, sheepLayerMask);

        if (nearbySheep.Length > 0)
        {
            // Calculate average position of nearby sheep
            Vector3 averagePosition = Vector3.zero;
            int count = 0;

            foreach (Collider col in nearbySheep)
            {
                if (col.transform != transform)
                {
                    averagePosition += col.transform.position;
                    count++;
                }
            }

            if (count > 0)
            {
                averagePosition /= count;

                // Flee in the opposite direction
                Vector3 fleeDirection = (transform.position - averagePosition).normalized;
                float fleeDistance = Random.Range(minFleeDistance, maxFleeDistance);
                targetPosition = transform.position + fleeDirection * fleeDistance;

                // Make sure the flee position is within arena bounds
                KeepPositionInArena(ref targetPosition);

                // Sync target position in network mode
                if (isNetworkMode && networkAI != null)
                {
                    networkAI.SyncTargetPosition(targetPosition);
                }
            }
            else
            {
                SetNewWanderTarget(); // Fallback
            }
        }
        else
        {
            SetNewWanderTarget(); // Fallback
        }
    }

    void LookForTarget()
    {
        // Only look for targets if we're specifically wandering
        if (currentState != AIState.Wander)
            return;

        // Find nearby sheep
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, detectionRadius, sheepLayerMask);

        // Filter out self
        List<Transform> potentialTargets = new List<Transform>();
        foreach (Collider col in hitColliders)
        {
            if (col.transform != transform && col.gameObject.activeSelf)
            {
                potentialTargets.Add(col.transform);
            }
        }

        // If we found potential targets
        if (potentialTargets.Count > 0)
        {
            // Sometimes randomly select a target, sometimes pick closest
            Transform selectedTarget;

            if (Random.value < 0.7f)
            {
                // Pick closest
                float closestDistance = float.MaxValue;
                selectedTarget = null;

                foreach (Transform target in potentialTargets)
                {
                    float distance = Vector3.Distance(transform.position, target.position);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        selectedTarget = target;
                    }
                }
            }
            else
            {
                // Pick random
                selectedTarget = potentialTargets[Random.Range(0, potentialTargets.Count)];
            }

            if (selectedTarget != null)
            {
                targetSheep = selectedTarget;

                // Decide whether to chase or flee based on personality and situation
                if (Random.value < fleeChance)
                {
                    SetFleeState();
                }
                else
                {
                    ChangeState(AIState.Chase);
                }
            }
        }
    }

    void Wander()
    {
        // If we've reached the target or don't have one, set a new one
        if (targetPosition == Vector3.zero ||
            Vector3.Distance(transform.position, targetPosition) < 1.5f)
        {
            SetNewWanderTarget();
        }

        // Rotate towards target with smooth turning
        RotateTowardsTarget(targetPosition, rotationSpeed);

        // Apply movement force - speed varies randomly for more natural movement
        float speedVariation = Random.Range(0.7f, 1.0f);
        MoveForward(accelerationForce * speedVariation);
    }

    void SetNewWanderTarget()
    {
        // Get a random point within arena bounds
        Vector3 arenaSize = Vector3.zero;
        Vector3 arenaCenter = Vector3.zero;

        if (gameManager != null)
        {
            arenaSize = gameManager.GetArenaSize();
            arenaCenter = gameManager.GetArenaCenter();
        }
        else
        {
            // Default arena size and center if gameManager not available
            arenaSize = new Vector3(30, 0, 30);
            arenaCenter = Vector3.zero;
        }

        // Try to find a position that's not too close to other sheep
        Vector3 newTarget = Vector3.zero;
        bool foundGoodPosition = false;
        int maxAttempts = 5;

        for (int i = 0; i < maxAttempts; i++)
        {
            // Get random position within arena
            float randomRadius = Random.Range(1f, wanderRadius);
            float randomAngle = Random.Range(0, 2f * Mathf.PI);

            float x = randomRadius * Mathf.Cos(randomAngle);
            float z = randomRadius * Mathf.Sin(randomAngle);

            newTarget = transform.position + new Vector3(x, 0, z);

            // Make sure it's within arena bounds
            newTarget.x = Mathf.Clamp(newTarget.x,
                arenaCenter.x - arenaSize.x / 2 + 1f,
                arenaCenter.x + arenaSize.x / 2 - 1f);

            newTarget.z = Mathf.Clamp(newTarget.z,
                arenaCenter.z - arenaSize.z / 2 + 1f,
                arenaCenter.z + arenaSize.z / 2 - 1f);

            // Check if position is far enough from other sheep
            bool tooClose = false;
            Collider[] nearbySheep = Physics.OverlapSphere(newTarget, personalSpaceRadius, sheepLayerMask);

            foreach (Collider col in nearbySheep)
            {
                if (col.transform != transform)
                {
                    tooClose = true;
                    break;
                }
            }

            if (!tooClose)
            {
                foundGoodPosition = true;
                break;
            }
        }

        if (!foundGoodPosition)
        {
            // Fallback to simple random position
            float x = Random.Range(-arenaSize.x / 2 + 1f, arenaSize.x / 2 - 1f) + arenaCenter.x;
            float z = Random.Range(-arenaSize.z / 2 + 1f, arenaSize.z / 2 - 1f) + arenaCenter.z;
            newTarget = new Vector3(x, transform.position.y, z);
        }

        targetPosition = newTarget;

        // Sync target position in network mode
        if (isNetworkMode && networkAI != null)
        {
            networkAI.SyncTargetPosition(targetPosition);
        }
    }

    // Set target position directly (called from NetworkAISheep)
    public void SetTargetPosition(Vector3 position)
    {
        targetPosition = position;
    }

    void KeepPositionInArena(ref Vector3 position)
    {
        if (gameManager == null) return;

        Vector3 arenaSize = gameManager.GetArenaSize();
        Vector3 arenaCenter = gameManager.GetArenaCenter();

        position.x = Mathf.Clamp(position.x,
            arenaCenter.x - arenaSize.x / 2 + 1f,
            arenaCenter.x + arenaSize.x / 2 - 1f);

        position.z = Mathf.Clamp(position.z,
            arenaCenter.z - arenaSize.z / 2 + 1f,
            arenaCenter.z + arenaSize.z / 2 - 1f);
    }

    void ChaseTarget()
    {
        // Make sure target is still valid
        if (targetSheep == null || !targetSheep.gameObject.activeSelf)
        {
            ChangeState(AIState.Wander);
            return;
        }

        // Check if target is still in range
        float distance = Vector3.Distance(transform.position, targetSheep.position);
        if (distance > detectionRadius * 1.5f)
        {
            ChangeState(AIState.Wander);
            return;
        }

        // If we're very close to another sheep but not targeting it, sometimes avoid collision
        Collider[] verySheep = Physics.OverlapSphere(transform.position, personalSpaceRadius, sheepLayerMask);
        foreach (Collider col in verySheep)
        {
            if (col.transform != transform && col.transform != targetSheep)
            {
                // 50% chance to change target to avoid collision
                if (Random.value < 0.5f)
                {
                    targetSheep = col.transform;
                    break;
                }
            }
        }

        // Rotate towards target with smooth turning
        RotateTowardsTarget(targetSheep.position, rotationSpeed * 1.2f);

        // Apply movement force - move faster when chasing
        MoveForward(accelerationForce * 1.2f);

        // If close enough and facing target, switch to charge state
        if (distance < chargeDistance &&
            Vector3.Angle(transform.forward, targetSheep.position - transform.position) < 20f)
        {
            PrepareCharge();
        }
    }

    void Flee()
    {
        // If we've reached the target, go back to wandering
        if (Vector3.Distance(transform.position, targetPosition) < 1.5f)
        {
            ChangeState(AIState.Wander);
            SetNewWanderTarget();
            return;
        }

        // Rotate towards target with smooth turning
        RotateTowardsTarget(targetPosition, rotationSpeed * 1.3f);

        // Apply movement force - move faster when fleeing
        MoveForward(accelerationForce * 1.3f);
    }

    void PrepareCharge()
    {
        // Play baa sound with pitch variation
        if (audioSource != null && baaSound != null)
        {
            // Apply random pitch variation
            audioSource.pitch = Random.Range(minPitch, maxPitch);

            // Configure audio range
            audioSource.rolloffMode = AudioRolloffMode.Custom;
            audioSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff, baaRolloffCurve);
            audioSource.minDistance = baaMinDistance;
            audioSource.maxDistance = baaMaxDistance;

            // Play the sound with specified volume
            audioSource.PlayOneShot(baaSound, baaVolume);

            // Reset pitch after playing
            audioSource.pitch = 1.0f;
        }

        // Set charge state with short delay
        ChangeState(AIState.Charge);
        stateTimer = chargeTime; // Brief charge-up time

        // Reset hit flag
        hasSucessfullyHit = false;

        // Sync the charge action in network mode
        if (isNetworkMode && networkAI != null)
        {
            networkAI.SyncChargeAction();
        }
    }

    // Method to execute a charge (can be called from network component)
    public void ExecuteCharge()
    {
        if (isDead || movementLocked) return;

        // Play baa sound (if not already played by server)
        if (!NetworkServer.active && audioSource != null && baaSound != null)
        {
            audioSource.pitch = Random.Range(minPitch, maxPitch);
            audioSource.PlayOneShot(baaSound, baaVolume);
            audioSource.pitch = 1.0f;
        }

        // Apply a strong forward force
        rb.AddForce(transform.forward * chargePower, ForceMode.Impulse);
    }

    void Charge()
    {
        ExecuteCharge();
    }

    void OnCollisionEnter(Collision collision)
    {
        if (isDead || movementLocked) return;

        // In network mode, only server processes collisions
        if (isNetworkMode && !NetworkServer.active)
        {
            return;
        }

        // Check if we hit another sheep with force
        if ((collision.gameObject.CompareTag("Sheep") || collision.gameObject.CompareTag("Player"))
            && rb.velocity.magnitude > 3f)
        {
            HandleSheepCollision(collision);
        }
    }

    // Extracted collision method that can be called from network
    public void HandleSheepCollision(Collision collision)
    {
        // Mark the collision time to temporarily disable some stabilization
        lastCollisionTime = Time.time;

        // Set the hit flag if we're charging
        if (currentState == AIState.Charge)
        {
            hasSucessfullyHit = true;
        }

        // Calculate impact force direction
        Vector3 impactDirection = collision.contacts[0].point - transform.position;
        impactDirection.y = 0; // Keep it on the horizontal plane

        // Apply force to the other sheep
        Rigidbody otherRb = collision.gameObject.GetComponent<Rigidbody>();
        if (otherRb != null)
        {
            // Calculate impact power based on relative velocity
            float impactPower = collision.relativeVelocity.magnitude;
            float finalForce = Mathf.Min(chargePower + impactPower * 0.5f, chargePower * 1.5f);

            // Apply horizontal force
            otherRb.AddForce(impactDirection.normalized * finalForce, ForceMode.Impulse);

            // Add rotational force to help knock it over
            Vector3 torqueDir = Vector3.Cross(impactDirection.normalized, Vector3.up).normalized;
            otherRb.AddTorque(torqueDir * finalForce * 0.8f, ForceMode.Impulse);

            // Mark the other sheep as recently hit
            AIPlayerController aiController = collision.gameObject.GetComponent<AIPlayerController>();
            if (aiController != null)
            {
                aiController.OnGotHit();
            }

            PlayerController playerController = collision.gameObject.GetComponent<PlayerController>();
            if (playerController != null)
            {
                playerController.OnGotHit();
            }

            // For network mode, sync the hit to other clients
            if (isNetworkMode && NetworkServer.active && networkAI != null)
            {
                // Find network components to sync the hit
                NetworkSheepPlayer otherNetworkPlayer = collision.gameObject.GetComponent<NetworkSheepPlayer>();
                if (otherNetworkPlayer != null)
                {
                    // Get direction and force
                    Vector3 direction = impactDirection.normalized;
                    // Use the generic sync hit entity method
                    networkAI.SyncHitNetworkEntity(otherNetworkPlayer.netId, direction, finalForce);
                }

                NetworkAISheep otherNetworkAI = collision.gameObject.GetComponent<NetworkAISheep>();
                if (otherNetworkAI != null)
                {
                    // Get direction and force
                    Vector3 direction = impactDirection.normalized;
                    // Use the generic sync hit entity method
                    networkAI.SyncHitNetworkEntity(otherNetworkAI.netId, direction, finalForce);
                }
            }
        }
    }

    public override void Die()
    {
        if (isDead) return; // Prevent multiple deaths

        base.Die();

        // For network mode, let server handle the death notification
        if (isNetworkMode && networkAI != null)
        {
            if (NetworkServer.active)
            {
                // Server notifies all clients about the death
                networkAI.SyncDeath();

                // Notify game manager
                if (gameManager != null)
                {
                    gameManager.SheepDied(gameObject);
                }
            }
        }
        else
        {
            // Regular offline notification handled by base class
        }

        // Destroy the game object after a delay in non-network mode
        if (!isNetworkMode)
        {
            Destroy(gameObject, 2f);
        }
    }
}