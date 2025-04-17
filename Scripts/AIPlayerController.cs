using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

public class AIPlayerController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float accelerationForce = 22f;
    public float maxSpeed = 9f;
    public float rotationSpeed = 130f;
    public float chargePower = 10f;
    public float chargeCooldown = 2f;

    [Header("Stability & Physics Settings")]
    public float centerOfMassHeight = -0.3f; // Lowered center of mass for stability
    public float uprightForce = 7f; // Force keeping the sheep upright
    public float groundCheckDistance = 0.3f; // Distance to check for ground
    public LayerMask groundLayer; // Layer for the ground
    public float groundedDrag = 0.8f; // Drag when on ground
    public float airDrag = 0.2f; // Drag when in air

    [Header("Advanced Stability")]
    public bool useStabilizationRays = true; // Use raycasts for extra stability
    public float stabilizationForce = 12f; // Force for ray-based stabilization
    public float maxTippingVelocity = 18f; // Speed where tipping protection is reduced
    public float selfRightingSpeed = 4f; // How fast the sheep self-rights

    [Header("Death Settings")]
    public float deathAngle = 85f; // Angle in degrees where sheep dies (laying on back)
    public float deathConfirmTime = 0.5f; // Time sheep must be on back before dying

    [Header("Impact Settings")]
    public float impactCenterOfMassYOffset = 0.5f; // How much to shift center of mass up on impact
    public float centerOfMassRecoveryTime = 0.8f; // How long until center of mass returns to normal

    [Header("AI Behavior Settings")]
    public float detectionRadius = 6f;
    public float minFleeDistance = 3f;
    public float maxFleeDistance = 8f;
    public float personalSpaceRadius = 2f; // Distance to keep from other sheep
    public float wanderRadius = 6f; // How far to wander from current position
    public float fleeChance = 0.3f; // Chance to flee instead of attack after spotting a target
    public float behaviorChangeInterval = 3f; // How often the AI can change its mind
    public float chargeDistance = 4f; // Distance at which to initiate a charge
    public LayerMask sheepLayerMask;

    [Header("References")]
    public GameObject explosionPrefab;
    public AudioClip baaSound;
    public AudioClip explosionSound;

    [Header("Audio Settings")]
    [Range(0.5f, 1.5f)]
    public float minPitch = 0.8f;
    [Range(0.5f, 1.5f)]
    public float maxPitch = 1.2f;
    public float audioSpatialBlend = 1.0f; // 1.0 = fully 3D sound

    [Header("Audio Range and Volume Settings")]
    [Tooltip("Volume of the baa sound (0-1)")]
    [Range(0f, 1f)]
    public float baaVolume = 0.8f;
    [Tooltip("Minimum distance at which the baa sound can be heard at full volume")]
    public float baaMinDistance = 1f;
    [Tooltip("Maximum distance at which the baa sound can be heard before fading out completely")]
    public float baaMaxDistance = 20f;
    [Tooltip("How quickly the baa sound fades with distance")]
    public AnimationCurve baaRolloffCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);

    [Tooltip("Volume of the explosion sound (0-1)")]
    [Range(0f, 1f)]
    public float explosionVolume = 1.0f;
    [Tooltip("Minimum distance at which the explosion sound can be heard at full volume")]
    public float explosionMinDistance = 1f;
    [Tooltip("Maximum distance at which the explosion sound can be heard before fading out completely")]
    public float explosionMaxDistance = 50f;
    [Tooltip("How quickly the explosion sound fades with distance")]
    public AnimationCurve explosionRolloffCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);

    [Header("Network Settings")]
    public bool isNetworkMode = false;
    private NetworkAISheep networkAI; // Reference to network component if present

    // Components
    private Rigidbody rb;
    private AudioSource audioSource;
    private float deathTimer = 0f;
    private float behaviorTimer = 0f;
    private Vector3 originalCenterOfMass;
    private Vector3 impactCenterOfMass;
    private bool centerOfMassShifted = false;

    // AI State variables
    public enum AIState { Wander, Chase, Charge, Flee, Recover }
    private AIState currentState = AIState.Wander;
    private Vector3 targetPosition;
    private Transform targetSheep;
    private float stateTimer = 0f;
    private bool hasSucessfullyHit = false;
    private float lastCollisionTime = -10f;
    private bool isGrounded = false;

    // Status
    private bool isDead = false;

    // Movement lock
    private bool movementLocked = true;

    // Cache game manager reference
    private GameManager gameManager;

    void Awake()
    {
        // Try to get network component if it exists
        networkAI = GetComponent<NetworkAISheep>();

        // Check if we're in network mode
        isNetworkMode = Mirror.NetworkManager.singleton != null &&
                   (Mirror.NetworkClient.active || Mirror.NetworkServer.active);
    }

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        audioSource = GetComponent<AudioSource>();
        gameManager = GameManager.Instance;

        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        // Configure audio source for 3D sound
        audioSource.spatialBlend = audioSpatialBlend;
        audioSource.rolloffMode = AudioRolloffMode.Linear;
        audioSource.minDistance = 10f;
        audioSource.maxDistance = 40f;
        audioSource.volume = 0.5f;

        // Store original center of mass for later restoration
        originalCenterOfMass = new Vector3(0, centerOfMassHeight, 0);
        impactCenterOfMass = new Vector3(0, centerOfMassHeight + impactCenterOfMassYOffset, 0);

        // Apply the lowered center of mass for stability
        rb.centerOfMass = originalCenterOfMass;

        // Optimize physics settings for stability
        rb.maxAngularVelocity = 7;

        // Set initial wander target
        SetNewWanderTarget();

        // Initialize behavior timer with random offset to desynchronize AI behavior
        behaviorTimer = Random.Range(0f, behaviorChangeInterval);

        // Start with movement locked - will be unlocked by the GameManager
        SetMovementLocked(true);

        // Check if game is already started (in case we join late)
        CheckGameStarted();

        // Make sure we're in the active sheep list
        if (gameManager != null && !gameManager.activeSheep.Contains(gameObject))
        {
            gameManager.activeSheep.Add(gameObject);
        }
    }

    // Check if the game has already started when we join
    void CheckGameStarted()
    {
        if (gameManager != null && gameManager.gameStarted)
        {
            // Game already started, unlock movement
            SetMovementLocked(false);
        }
        else
        {
            // Start listening for game start event
            StartCoroutine(CheckForGameStart());
        }
    }

    // Coroutine to check for game start
    IEnumerator CheckForGameStart()
    {
        while (movementLocked)
        {
            if (gameManager != null && gameManager.gameStarted)
            {
                SetMovementLocked(false);
                yield break;
            }
            yield return new WaitForSeconds(0.2f);
        }
    }

    // Method to lock/unlock movement - called by GameManager or NetworkManager
    public void SetMovementLocked(bool locked)
    {
        movementLocked = locked;

        // If locked, zero out velocity
        if (locked && rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        Debug.Log($"AI movement {(locked ? "locked" : "unlocked")}");
    }

    void Update()
    {
        if (isDead || movementLocked) return;

        // In network mode, only the server controls AI behavior
        if (isNetworkMode)
        {
            if (!NetworkServer.active)
            {
                return;
            }
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

    void FixedUpdate()
    {
        if (isDead || movementLocked) return;

        // In network mode, only the server controls AI physics
        if (isNetworkMode && !NetworkServer.active)
        {
            return;
        }

        // Check if grounded
        CheckGrounded();

        // Apply appropriate drag based on ground contact
        rb.drag = isGrounded ? groundedDrag : airDrag;

        // Death check - only proceed if AI has been upside down for a certain time
        float uprightness = Vector3.Dot(transform.up, Vector3.up);
        float currentAngle = Mathf.Acos(Mathf.Clamp01(uprightness)) * Mathf.Rad2Deg;

        if (currentAngle > deathAngle)
        {
            deathTimer += Time.fixedDeltaTime;
            if (deathTimer >= deathConfirmTime)
            {
                Die();
                return;
            }
        }
        else
        {
            deathTimer = 0f;
        }

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

        // Apply stabilization to keep the sheep upright
        ApplyStabilization();

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

    void CheckGrounded()
    {
        isGrounded = Physics.Raycast(
            transform.position,
            Vector3.down,
            groundCheckDistance,
            groundLayer
        );
    }

    void ApplyStabilization()
    {
        // Calculate how fast we're going (ground speed only)
        Vector3 horizontalVelocity = new Vector3(rb.velocity.x, 0, rb.velocity.z);
        float speedFactor = Mathf.Clamp01(horizontalVelocity.magnitude / maxTippingVelocity);

        // The faster we go, the less upright force we apply (to allow fun knockovers from charges)
        float upForce = uprightForce * (1f - speedFactor * 0.7f);

        // Apply much weaker stabilization when recently hit
        if (Time.time < lastCollisionTime + 1.0f)
        {
            // Reduce stabilization by 80% for a short time after being hit
            upForce *= 0.2f;
        }
        else if (Time.time < lastCollisionTime + 2.0f)
        {
            // Gradually restore stability between 1-2 seconds after hit
            float recoveryPhase = (Time.time - lastCollisionTime - 1.0f) / 1.0f; // 0 to 1
            upForce *= Mathf.Lerp(0.2f, 1.0f, recoveryPhase);
        }

        // Only apply strong stabilization when not charging
        if (currentState != AIState.Charge)
        {
            // Get current up vector
            Vector3 currentUp = transform.up;

            // Calculate the torque needed to keep sheep upright
            Vector3 targetUp = Vector3.up;
            Vector3 rotationAxis = Vector3.Cross(currentUp, targetUp);
            float rotationAngle = Vector3.Angle(currentUp, targetUp);

            // Apply self-righting torque (stronger when more tilted)
            if (rotationAngle > 5f)
            {
                float torqueFactor = rotationAngle / 90f; // 0 to 1 based on tilt
                rb.AddTorque(rotationAxis.normalized * torqueFactor * upForce * selfRightingSpeed, ForceMode.Force);
            }

            // Additional ray-based stability when needed
            if (useStabilizationRays && isGrounded)
            {
                // Also reduce ray stabilization strength when recently hit
                float rayStabStrength = stabilizationForce;
                if (Time.time < lastCollisionTime + 1.5f)
                {
                    rayStabStrength *= 0.2f; // 80% reduction when recently hit
                }

                ApplyStabilizationRays(rayStabStrength);
            }
        }
    }

    void ApplyStabilizationRays(float stabilizationStrength)
    {
        // Cast rays from the sides of the sheep to find potential tipping points
        Vector3 right = transform.right;
        Vector3 left = -transform.right;

        float rayLength = 1f;
        bool hitRight = Physics.Raycast(transform.position, right, out RaycastHit rightHit, rayLength, groundLayer);
        bool hitLeft = Physics.Raycast(transform.position, left, out RaycastHit leftHit, rayLength, groundLayer);

        // If one side is going to hit the ground but not the other, apply counter-force
        if (hitRight && !hitLeft && rightHit.distance < 0.5f)
        {
            // About to tip right, push back left
            rb.AddForce(-right * stabilizationStrength, ForceMode.Force);
        }
        else if (hitLeft && !hitRight && leftHit.distance < 0.5f)
        {
            // About to tip left, push back right
            rb.AddForce(-left * stabilizationStrength, ForceMode.Force);
        }
    }

    void ApplyRecoveryForces()
    {
        // Calculate recovery direction
        Vector3 currentUp = transform.up;
        Vector3 targetUp = Vector3.up;
        Vector3 rotationAxis = Vector3.Cross(currentUp, targetUp);

        // Apply strong self-righting torque
        float upFactor = Vector3.Dot(transform.up, Vector3.up);
        float recoveryStrength = Mathf.Clamp01(1f - upFactor) * 2f; // Stronger when more tilted

        rb.AddTorque(rotationAxis.normalized * uprightForce * recoveryStrength, ForceMode.Force);

        // Add a bit of upward force but not enough to make it fly
        if (transform.position.y < 0.5f)
        {
            rb.AddForce(Vector3.up * uprightForce * 0.5f, ForceMode.Force);
        }
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
        stateTimer = 0.3f; // Brief charge-up time

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

        // Allow more tipping during charge
        lastCollisionTime = Time.time;
    }

    void Charge()
    {
        ExecuteCharge();
    }

    void RotateTowardsTarget(Vector3 targetPos, float turnSpeed)
    {
        // Calculate direction to target
        Vector3 direction = targetPos - transform.position;
        direction.y = 0; // Keep rotation on horizontal plane

        // Skip if direction is zero
        if (direction == Vector3.zero)
            return;

        // Calculate angle to target
        float angleToTarget = Vector3.Angle(transform.forward, direction);

        // Significantly reduce rotation speed for sharp turns
        float adjustedRotationSpeed = turnSpeed;
        if (angleToTarget > 60f)
        {
            adjustedRotationSpeed *= 0.5f; // Much slower for sharp turns
        }
        else if (angleToTarget > 30f)
        {
            adjustedRotationSpeed *= 0.7f; // Somewhat slower for medium turns
        }

        // Limit maximum turning rate to make it more natural
        float maxTurnPerFrame = 1.0f; // Degrees per frame
        float maxRotation = maxTurnPerFrame * (adjustedRotationSpeed / 100f) * (Time.deltaTime * 60f); // Normalized for 60 fps

        // Calculate target rotation with limits
        Quaternion currentRotation = transform.rotation;
        Quaternion targetRotation = Quaternion.LookRotation(direction);

        // Use RotateTowards instead of Slerp for more controlled rotation
        transform.rotation = Quaternion.RotateTowards(
            currentRotation,
            targetRotation,
            maxRotation
        );
    }

    void MoveForward(float force)
    {
        // Only apply movement if somewhat upright
        float uprightness = Vector3.Dot(transform.up, Vector3.up);

        if (uprightness > 0.7f) // About 45 degrees from upright
        {
            // Apply force
            rb.AddForce(transform.forward * force, ForceMode.Acceleration);

            // Limit speed
            Vector3 horizontalVelocity = new Vector3(rb.velocity.x, 0, rb.velocity.z);
            if (horizontalVelocity.magnitude > maxSpeed)
            {
                horizontalVelocity = horizontalVelocity.normalized * maxSpeed;
                rb.velocity = new Vector3(horizontalVelocity.x, rb.velocity.y, horizontalVelocity.z);
            }

            // Add stabilizing downward force
            rb.AddForce(Vector3.down * force * 0.4f, ForceMode.Force);
        }
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

    // Called when this sheep gets hit by another sheep
    public void OnGotHit()
    {
        if (isDead || movementLocked) return;

        // Mark the collision time to temporarily disable stability systems
        lastCollisionTime = Time.time;

        // Temporarily increase max angular velocity to allow more rotation
        rb.maxAngularVelocity = 20f; // Increased for more dramatic knockdowns

        // Reduce drag to make it slide more
        float originalDrag = rb.drag;
        rb.drag = originalDrag * 0.2f;

        // Apply a strong destabilizing force
        Vector3 randomDirection = new Vector3(Random.Range(-1f, 1f), 0, Random.Range(-1f, 1f)).normalized;
        rb.AddTorque(randomDirection * 10f, ForceMode.Impulse);

        // Shift center of mass upward to make tipping easier
        rb.centerOfMass = impactCenterOfMass;
        centerOfMassShifted = true;

        // Switch to recovery state with delay to ensure it doesn't try to recover too quickly
        ChangeState(AIState.Recover);
        stateTimer = 1.5f;

        // Schedule stability to be restored
        StartCoroutine(RestoreStabilityAfterDelay(centerOfMassRecoveryTime, originalDrag));
    }

    // Method that can be called from network to apply a hit
    public void ApplyNetworkHitForce(Vector3 direction, float force)
    {
        if (isDead || movementLocked) return;

        OnGotHit();

        // Apply network-synced force
        if (rb != null)
        {
            // Apply both direct force and torque
            rb.AddForce(direction * force, ForceMode.Impulse);

            Vector3 torqueDir = Vector3.Cross(direction.normalized, Vector3.up).normalized;
            rb.AddTorque(torqueDir * force * 0.8f, ForceMode.Impulse);
        }
    }

    IEnumerator RestoreStabilityAfterDelay(float delay, float originalDrag)
    {
        yield return new WaitForSeconds(delay);

        // Restore physics properties
        rb.maxAngularVelocity = 7f;
        rb.drag = originalDrag;

        // Gradually restore center of mass
        if (centerOfMassShifted)
        {
            float transitionTime = 0.3f; // Time to smoothly transition center of mass
            float elapsedTime = 0f;

            while (elapsedTime < transitionTime)
            {
                float t = elapsedTime / transitionTime;
                rb.centerOfMass = Vector3.Lerp(impactCenterOfMass, originalCenterOfMass, t);
                elapsedTime += Time.deltaTime;
                yield return null;
            }

            rb.centerOfMass = originalCenterOfMass;
            centerOfMassShifted = false;
        }
    }

    public void Die()
    {
        if (isDead) return; // Prevent multiple deaths

        isDead = true;

        // Play death effects
        PlayDeathEffects();

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
            // Regular offline notification
            if (gameManager != null)
            {
                gameManager.SheepDied(gameObject);
            }
        }

        // Disable mesh renderer but keep collider temporarily
        MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>();
        foreach (MeshRenderer renderer in renderers)
        {
            renderer.enabled = false;
        }

        // Destroy the game object after a delay
        if (!isNetworkMode || NetworkServer.active)
        {
            Destroy(gameObject, 2f);
        }
    }

    // Play death effects without triggering network messages (can be called separately)
    public void PlayDeathEffects()
    {
        // Play explosion sound
        if (audioSource != null && explosionSound != null)
        {
            // Configure audio range
            audioSource.rolloffMode = AudioRolloffMode.Custom;
            audioSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff, explosionRolloffCurve);
            audioSource.minDistance = explosionMinDistance;
            audioSource.maxDistance = explosionMaxDistance;

            // Play the sound with specified volume
            audioSource.PlayOneShot(explosionSound, explosionVolume);
        }

        // Instantiate explosion
        if (explosionPrefab != null)
        {
            Instantiate(explosionPrefab, transform.position, Quaternion.identity);
        }
    }
}