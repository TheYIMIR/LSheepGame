using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

/// <summary>
/// Base class for all sheep controllers, handling common physics, stability, and death functionality.
/// This class is inherited by both AIPlayerController and PlayerController.
/// </summary>
public abstract class SheepController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float accelerationForce = 20f;
    public float maxSpeed = 7f;
    public float rotationSpeed = 150f;
    public float chargePower = 10f;
    public float chargeCooldown = 2f;

    [Header("Stability Settings")]
    public float centerOfMassHeight = -0.3f;
    public float uprightForce = 7f;
    public float groundCheckDistance = 0.3f;
    public LayerMask groundLayer;
    public float groundedDrag = 0.8f;
    public float airDrag = 0.2f;
    public float antiSpinFactor = 5f;
    public float maxAllowedAngularVelocity = 2f;
    public bool useStabilizationRays = true;
    public float stabilizationForce = 12f;
    public float maxTippingVelocity = 18f;
    public float selfRightingSpeed = 4f;

    [Header("Death Settings")]
    public float deathAngle = 85f;
    public float deathConfirmTime = 0.5f;

    [Header("Impact Settings")]
    public float impactCenterOfMassYOffset = 0.5f;
    public float centerOfMassRecoveryTime = 0.8f;

    [Header("Network Smoothing")]
    public float positionSmoothTime = 0.1f; // Time to smooth position transitions
    public float rotationSmoothTime = 0.1f; // Time to smooth rotation transitions
    public bool useNetworkSmoothing = true; // Toggle for network smoothing
    public float networkSmoothingThreshold = 0.15f; // Distance threshold for position smoothing
    public float maxSmoothingSpeed = 10f; // Maximum velocity for smoothing

    [Header("References")]
    public GameObject explosionPrefab;
    public AudioClip baaSound;
    public AudioClip explosionSound;

    [Header("Audio Settings")]
    [Range(0.5f, 1.5f)]
    public float minPitch = 0.8f;
    [Range(0.5f, 1.5f)]
    public float maxPitch = 1.2f;
    public float audioSpatialBlend = 1.0f;

    [Range(0f, 1f)]
    public float baaVolume = 0.8f;
    public float baaMinDistance = 1f;
    public float baaMaxDistance = 20f;
    public AnimationCurve baaRolloffCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);

    [Range(0f, 1f)]
    public float explosionVolume = 1.0f;
    public float explosionMinDistance = 1f;
    public float explosionMaxDistance = 50f;
    public AnimationCurve explosionRolloffCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);

    [Header("Network Settings")]
    public bool isNetworkMode = false;

    // Protected components accessible to derived classes
    protected Rigidbody rb;
    protected AudioSource audioSource;
    protected Vector3 originalCenterOfMass;
    protected Vector3 impactCenterOfMass;
    protected bool centerOfMassShifted = false;
    protected float lastCollisionTime = -10f;
    protected float deathTimer = 0f;
    protected bool isGrounded = false;
    protected GameManager gameManager;

    // Network smoothing variables
    protected Vector3 targetPosition;
    protected Quaternion targetRotation;
    protected Vector3 positionVelocity; // For SmoothDamp
    protected Vector3 previousPosition;
    protected float previousFixedTime;
    protected bool hasInitialPosition = false;

    // Status
    protected bool isDead = false;
    protected bool movementLocked = true;

    protected virtual void Awake()
    {
        // Check if we're in network mode
        isNetworkMode = NetworkManager.singleton != null &&
                  (NetworkClient.active || NetworkServer.active);
    }

    protected virtual void Start()
    {
        rb = GetComponent<Rigidbody>();
        audioSource = GetComponent<AudioSource>();
        gameManager = GameManager.Instance;

        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        // Configure audio source for 3D sound
        ConfigureAudioSource();

        // Store original center of mass for later restoration
        originalCenterOfMass = new Vector3(0, centerOfMassHeight, 0);
        impactCenterOfMass = new Vector3(0, centerOfMassHeight + impactCenterOfMassYOffset, 0);

        // Apply the lowered center of mass for stability
        rb.centerOfMass = originalCenterOfMass;

        // Optimize physics settings for stability
        rb.maxAngularVelocity = 7;

        // Initialize network smoothing variables
        targetPosition = transform.position;
        targetRotation = transform.rotation;
        previousPosition = transform.position;
        previousFixedTime = Time.time;
        hasInitialPosition = true;

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

    protected virtual void ConfigureAudioSource()
    {
        if (audioSource == null) return;

        audioSource.spatialBlend = audioSpatialBlend;
        audioSource.rolloffMode = AudioRolloffMode.Linear;
        audioSource.minDistance = 10f;
        audioSource.maxDistance = 40f;
        audioSource.volume = 0.5f;
    }

    protected virtual void CheckGameStarted()
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

    protected virtual IEnumerator CheckForGameStart()
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

    public virtual void SetMovementLocked(bool locked)
    {
        movementLocked = locked;

        // If locked, zero out velocity
        if (locked && rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    // Update is called once per frame
    protected virtual void Update()
    {
        if (isDead || movementLocked) return;

        // Only apply network smoothing on clients for non-local entities
        if (isNetworkMode && !ShouldHandlePhysics() && useNetworkSmoothing)
        {
            PerformNetworkSmoothing();
        }
    }

    // Handle network position smoothing
    protected virtual void PerformNetworkSmoothing()
    {
        // Skip if we don't have a target position or we shouldn't be smoothing
        if (!hasInitialPosition) return;

        // Calculate how much the position has changed
        float distanceToTarget = Vector3.Distance(transform.position, targetPosition);

        // Only smooth if distance is above threshold but not too extreme (which could indicate teleportation)
        if (distanceToTarget > networkSmoothingThreshold && distanceToTarget < 5f)
        {
            // Smoothly move toward target position
            transform.position = Vector3.SmoothDamp(
                transform.position,
                targetPosition,
                ref positionVelocity,
                positionSmoothTime,
                maxSmoothingSpeed);

            // Smoothly rotate toward target rotation
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                Time.deltaTime / rotationSmoothTime);
        }
        // For large distances, just snap to avoid elastic band effect
        else if (distanceToTarget >= 5f)
        {
            transform.position = targetPosition;
            transform.rotation = targetRotation;
            positionVelocity = Vector3.zero;
        }
    }

    // Set the target position and rotation for network smoothing
    public void SetNetworkTarget(Vector3 position, Quaternion rotation)
    {
        if (!useNetworkSmoothing) return;

        // Calculate estimated velocity from network updates
        float deltaTime = Time.time - previousFixedTime;
        if (deltaTime > 0)
        {
            // Use position change to estimate current velocity
            Vector3 estimatedVelocity = (position - previousPosition) / deltaTime;

            // Apply estimated velocity to position for better prediction
            targetPosition = position + (estimatedVelocity * Time.deltaTime);
        }
        else
        {
            targetPosition = position;
        }

        targetRotation = rotation;
        hasInitialPosition = true;

        // Store previous values for next calculation
        previousPosition = position;
        previousFixedTime = Time.time;
    }

    protected virtual void FixedUpdate()
    {
        if (isDead || movementLocked) return;

        // Check if we should handle physics in this frame (for network authority)
        if (!ShouldHandlePhysics())
            return;

        // Check if grounded
        CheckGrounded();

        // Apply appropriate drag based on ground contact
        rb.drag = isGrounded ? groundedDrag : airDrag;

        // Death check - only proceed if sheep has been upside down for a certain time
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

        // Apply stabilization to keep the sheep upright
        ApplyStabilization(currentAngle);

        // Perform derived class specific physics updates
        PerformStatePhysicsUpdate(currentAngle);

        // If this is a server or local authority, record position for clients
        if (isNetworkMode && ShouldHandlePhysics())
        {
            // This position/rotation will be sent to other clients via NetworkTransform component
            // or custom sync code in the derived classes
        }
    }

    protected virtual bool ShouldHandlePhysics()
    {
        // Let derived classes override this for network authority checks
        return true;
    }

    protected void CheckGrounded()
    {
        isGrounded = Physics.Raycast(
            transform.position,
            Vector3.down,
            groundCheckDistance,
            groundLayer
        );
    }

    protected virtual void ApplyStabilization(float currentAngle)
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
        if (ShouldApplyStrongStabilization())
        {
            ApplySelfRightingTorque(upForce);

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

    protected virtual bool ShouldApplyStrongStabilization()
    {
        // Can be overridden by derived classes for charging state check
        return true;
    }

    protected void ApplySelfRightingTorque(float upForce)
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
    }

    protected void ApplyStabilizationRays(float stabilizationStrength)
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

    protected void ApplyRecoveryForces()
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

    // Abstract method to be implemented by derived classes
    protected abstract void PerformStatePhysicsUpdate(float currentAngle);

    // Method for rotating towards a target 
    protected void RotateTowardsTarget(Vector3 targetPos, float turnSpeed)
    {
        // Calculate direction to target
        Vector3 direction = targetPos - transform.position;
        direction.y = 0; // Keep rotation on horizontal plane

        // Skip if direction is zero
        if (direction == Vector3.zero)
            return;

        // Get current horizontal velocity and speed
        Vector3 horizontalVelocity = new Vector3(rb.velocity.x, 0, rb.velocity.z);
        float currentSpeed = horizontalVelocity.magnitude;
        
        // Reduce turn speed at higher speeds for more natural movement
        float speedFactor = currentSpeed / maxSpeed;
        float adjustedTurnSpeed = turnSpeed * Mathf.Lerp(1f, 0.5f, speedFactor);

        // Calculate angle to target
        float angleToTarget = Vector3.Angle(transform.forward, direction);

        // Significantly reduce rotation speed for sharp turns to avoid jerky movement
        if (angleToTarget > 60f)
        {
            adjustedTurnSpeed *= 0.4f; // Much slower for sharp turns
        }
        else if (angleToTarget > 30f)
        {
            adjustedTurnSpeed *= 0.6f; // Somewhat slower for medium turns
        }
        
        // Slow down during sharp turns to make movement more natural
        if (angleToTarget > 45f && currentSpeed > maxSpeed * 0.5f)
        {
            // Apply braking force during sharp turns
            float brakeFactor = Mathf.Lerp(0f, 0.3f, (angleToTarget - 45f) / 45f);
            rb.AddForce(-horizontalVelocity.normalized * brakeFactor * accelerationForce, ForceMode.Force);
        }

        // Limit maximum turning rate to make it more natural
        float maxTurnPerFrame = 1.0f; // Degrees per frame
        float maxRotation = maxTurnPerFrame * (adjustedTurnSpeed / 100f) * (Time.deltaTime * 60f); // Normalized for 60 fps

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

    // Method for moving forward with speed limiting
    protected void MoveForward(float force)
    {
        // Only apply movement if somewhat upright
        float uprightness = Vector3.Dot(transform.up, Vector3.up);

        if (uprightness > 0.7f) // About 45 degrees from upright
        {
            // Get current horizontal velocity
            Vector3 horizontalVelocity = new Vector3(rb.velocity.x, 0, rb.velocity.z);
            float currentSpeed = horizontalVelocity.magnitude;
            
            // Calculate a smoother force application that decreases as we approach max speed
            float speedFactor = 1f - Mathf.Clamp01(currentSpeed / maxSpeed);
            float adjustedForce = force * Mathf.Lerp(0.5f, 1f, speedFactor);
            
            // Apply force with smooth falloff near max speed
            rb.AddForce(transform.forward * adjustedForce, ForceMode.Acceleration);

            // Apply more gentle speed limiting to avoid jerky motion
            if (currentSpeed > maxSpeed)
            {
                // Calculate a damping factor - stronger the further we are over the speed limit
                float overSpeedFactor = (currentSpeed - maxSpeed) / maxSpeed;
                float dampingForce = overSpeedFactor * 0.8f;
                
                // Apply counter-force proportional to how much we're over the speed limit
                Vector3 dampingDirection = -horizontalVelocity.normalized;
                rb.AddForce(dampingDirection * force * dampingForce, ForceMode.Force);
            }

            // Add stabilizing downward force that increases with speed
            float downForceFactor = Mathf.Lerp(0.2f, 0.5f, currentSpeed / maxSpeed);
            rb.AddForce(Vector3.down * force * downForceFactor, ForceMode.Force);
        }
    }

    protected void ApplyAdditionalStabilizationAtSpeed(float currentAngle)
    {
        // Only apply if needed
        if (rb.velocity.magnitude < maxSpeed * 0.6f) return;
        
        // Get current horizontal velocity
        Vector3 horizontalVelocity = new Vector3(rb.velocity.x, 0, rb.velocity.z);
        float currentSpeed = horizontalVelocity.magnitude;
        
        // Calculate how aligned we are with our velocity
        float alignmentWithVelocity = Vector3.Dot(transform.forward.normalized, horizontalVelocity.normalized);
        
        // If moving fast and not aligned with velocity, apply stabilizing torque
        if (currentSpeed > maxSpeed * 0.6f && alignmentWithVelocity < 0.8f)
        {
            // Calculate the rotation needed to align with velocity
            Vector3 moveDirection = horizontalVelocity.normalized;
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
            
            // Apply a small torque to align with movement direction
            Vector3 rotationDifference = CalculateTorqueToReachRotation(targetRotation);
            float alignmentForce = Mathf.Lerp(0f, 0.5f, (currentSpeed - (maxSpeed * 0.6f)) / (maxSpeed * 0.4f));
            
            rb.AddTorque(rotationDifference * alignmentForce, ForceMode.Acceleration);
        }
        
        // Additional downforce at high speeds
        if (currentSpeed > maxSpeed * 0.8f)
        {
            // Apply extra downward force to prevent bouncing at high speeds
            float downForce = Mathf.Lerp(0f, uprightForce * 0.5f, (currentSpeed - (maxSpeed * 0.8f)) / (maxSpeed * 0.2f));
            rb.AddForce(Vector3.down * downForce, ForceMode.Force);
        }
    }

    private Vector3 CalculateTorqueToReachRotation(Quaternion targetRotation)
    {
        // Calculate rotation difference
        Quaternion rotationDifference = targetRotation * Quaternion.Inverse(transform.rotation);
        
        // Convert to axis angle representation
        rotationDifference.ToAngleAxis(out float angle, out Vector3 axis);
        
        // Ensure angle is between -180 and 180 degrees
        if (angle > 180f)
        {
            angle -= 360f;
        }
        
        // Calculate required torque based on angle
        return axis.normalized * (angle * Mathf.Deg2Rad);
    }

    // Method called when this sheep gets hit by another sheep
    public virtual void OnGotHit()
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

        // Schedule stability to be restored
        StartCoroutine(RestoreStabilityAfterDelay(centerOfMassRecoveryTime, originalDrag));
    }

    // Method for applying network synchronized hit force
    public virtual void ApplyNetworkHitForce(Vector3 direction, float force)
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

    protected IEnumerator RestoreStabilityAfterDelay(float delay, float originalDrag)
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

    public virtual void Die()
    {
        if (isDead) return; // Prevent multiple deaths

        isDead = true;

        // Play death effects
        PlayDeathEffects();

        // Disable mesh renderers to make the sheep invisible
        MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>();
        foreach (MeshRenderer renderer in renderers)
        {
            renderer.enabled = false;
        }

        // Specifically disable all box colliders
        BoxCollider[] boxColliders = GetComponentsInChildren<BoxCollider>();
        foreach (BoxCollider collider in boxColliders)
        {
            collider.enabled = false;
        }

        // Set rigidbody to kinematic to prevent further movement
        if (rb != null)
        {
            rb.isKinematic = true;

            // Zero out velocities as well
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // Handle local death notifications - network handling in derived classes
        if (!isNetworkMode)
        {
            if (gameManager != null)
            {
                gameManager.SheepDied(gameObject);
            }
        }

        // Disable this component after a delay
        Invoke("DisableController", 1f);
    }

    // Play death effects without triggering network messages (can be called separately)
    public virtual void PlayDeathEffects()
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

    protected virtual void DisableController()
    {
        this.enabled = false;
    }
}