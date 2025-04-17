using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

public class PlayerController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float accelerationForce = 20f;
    public float maxSpeed = 7f;
    public float rotationSpeed = 150f;
    public float stabilityFactor = 1.2f; // Increased stability factor
    public float chargePower = 10f;
    public float chargeTime = 1.5f;
    public float chargeCooldown = 2f;

    [Header("Stability Settings")]
    public float antiSpinFactor = 5f; // How strongly to resist spinning
    public float groundStickForce = 3f; // Force to keep sheep on ground when flipped
    public float maxRollAngle = 40f; // Max angle before sheep fights to stay upright
    public float maxAllowedAngularVelocity = 2f; // Max angular velocity when not hit
    public float centerOfMassHeight = -0.3f; // Lowered center of mass for stability

    [Header("Recovery Settings")]
    public float recoveryForce = 5f;
    public float recoveryTorque = 3f;
    public float criticalAngle = 60f; // Angle in degrees considered "about to fall over"
    public float deathAngle = 85f; // Angle in degrees where sheep dies (laying on back)

    [Header("Impact Settings")]
    public float impactCenterOfMassYOffset = 0.5f; // How much to shift center of mass up on impact
    public float centerOfMassRecoveryTime = 0.8f; // How long until center of mass returns to normal

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
    private NetworkSheepPlayer networkPlayer; // Reference to network component if present

    // Components
    private Rigidbody rb;
    private AudioSource audioSource;
    private bool isCharging = false;
    private bool canCharge = true;
    private float currentChargeTime = 0f;

    // Status
    private bool isDead = false;
    private bool isRecovering = false;
    private float lastCollisionTime = -10f; // Time tracking for collision-based spinning
    private Vector3 originalCenterOfMass;
    private Vector3 impactCenterOfMass;
    private bool centerOfMassShifted = false;

    // Movement lock
    private bool movementLocked = true;

    // Cache game manager reference
    private GameManager gameManager;

    void Awake()
    {
        // Try to get network component if it exists
        networkPlayer = GetComponent<NetworkSheepPlayer>();

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

        Debug.Log($"Player movement {(locked ? "locked" : "unlocked")}");
    }

    void Update()
    {
        if (isDead || movementLocked) return;

        // In network mode, only process input for local player
        if (isNetworkMode && (networkPlayer == null || !networkPlayer.isLocalPlayer))
        {
            return;
        }

        // Handle rotation input only if the sheep is relatively stable
        float horizontalInput = Input.GetAxis("Horizontal");
        transform.Rotate(0, horizontalInput * rotationSpeed * Time.deltaTime, 0);

        // Handle charging
        if (Input.GetKeyDown(KeyCode.Space) && canCharge && !isCharging && !isRecovering)
        {
            StartCoroutine(Charge());
        }
    }

    void FixedUpdate()
    {
        if (isDead || movementLocked) return;

        // In network mode, only process physics for local player or on server
        if (isNetworkMode && networkPlayer != null)
        {
            if (!networkPlayer.isLocalPlayer && !NetworkServer.active)
            {
                return;
            }
        }

        // Measure how upright the sheep is (1 = fully upright, 0 = sideways, -1 = upside down)
        float uprightness = Vector3.Dot(transform.up, Vector3.up);
        float currentAngle = Mathf.Acos(uprightness) * Mathf.Rad2Deg;

        // If sheep is on its back (upside down beyond death angle)
        if (currentAngle > deathAngle)
        {
            Die();
            return;
        }

        // Check if we need to enter recovery mode
        if (currentAngle > maxRollAngle && currentAngle < deathAngle)
        {
            isRecovering = true;
            TryToRecover();
        }
        else
        {
            isRecovering = false;
        }

        // Apply anti-spin when not recently hit by another sheep
        if (Time.time > lastCollisionTime + 0.5f)
        {
            // Apply angular velocity dampening to prevent excessive spinning
            if (rb.angularVelocity.magnitude > maxAllowedAngularVelocity)
            {
                rb.angularVelocity = Vector3.Lerp(
                    rb.angularVelocity,
                    rb.angularVelocity.normalized * maxAllowedAngularVelocity,
                    antiSpinFactor * Time.deltaTime
                );
            }
        }

        // Keep the sheep grounded when trying to flip or recover
        if (currentAngle > maxRollAngle * 0.5f || isRecovering)
        {
            // Apply downward force to prevent flying when flipped
            rb.AddForce(Vector3.down * groundStickForce * rb.mass, ForceMode.Force);

            // Limit upward velocity
            if (rb.velocity.y > 0.5f)
            {
                Vector3 v = rb.velocity;
                v.y *= 0.8f;
                rb.velocity = v;
            }
        }

        // Handle movement if not in recovery mode
        if (!isRecovering)
        {
            float verticalInput = 0;

            // In network mode, only get input for local player
            if (!isNetworkMode || (networkPlayer != null && networkPlayer.isLocalPlayer))
            {
                verticalInput = Input.GetAxis("Vertical");
            }

            // Only apply movement forces if somewhat upright
            if (uprightness > 0.7f) // About 45 degrees or less from upright
            {
                // Calculate movement force
                Vector3 movementForce = transform.forward * verticalInput * accelerationForce;

                // Apply force for acceleration-based movement
                rb.AddForce(movementForce, ForceMode.Acceleration);

                // Add stabilizing downward force while moving to prevent tipping
                if (verticalInput != 0)
                {
                    rb.AddForce(Vector3.down * accelerationForce * stabilityFactor, ForceMode.Force);
                }

                // Limit maximum speed (horizontal only)
                Vector3 horizontalVelocity = new Vector3(rb.velocity.x, 0, rb.velocity.z);
                if (horizontalVelocity.magnitude > maxSpeed)
                {
                    horizontalVelocity = horizontalVelocity.normalized * maxSpeed;
                    rb.velocity = new Vector3(horizontalVelocity.x, rb.velocity.y, horizontalVelocity.z);
                }
            }
            else if (uprightness > 0.4f) // Partially tipped but not in full recovery
            {
                // Apply reduced movement if partially tipped
                float verticalInputReduced = verticalInput * 0.5f;
                Vector3 movementForce = transform.forward * verticalInputReduced * accelerationForce;
                rb.AddForce(movementForce, ForceMode.Acceleration);
            }

            // Always reduce rolling when moving
            if (Mathf.Abs(verticalInput) > 0.1f)
            {
                // Dampen rotation around the forward axis to prevent barrel rolling
                Vector3 angVel = rb.angularVelocity;
                Vector3 forwardDirection = transform.forward;
                float forwardAngVel = Vector3.Dot(angVel, forwardDirection);
                rb.angularVelocity -= forwardDirection * forwardAngVel * 0.9f * Time.deltaTime;
            }
        }
    }

    void TryToRecover()
    {
        // Apply upward torque to try to right the sheep
        Vector3 currentUp = transform.up;
        Vector3 targetUp = Vector3.up;
        Vector3 torqueDirection = Vector3.Cross(currentUp, targetUp).normalized;

        // Apply recovery torque with more force when more tipped over
        float uprightness = Vector3.Dot(transform.up, Vector3.up);
        float recoveryMultiplier = Mathf.Clamp01(1.0f - uprightness) * 2f; // More force as sheep is more tipped

        rb.AddTorque(torqueDirection * recoveryTorque * recoveryMultiplier, ForceMode.Force);

        // Apply a bit of upward force to help with recovery, but not enough to make the sheep fly
        float height = transform.position.y;
        float heightLimit = 1.5f; // Maximum height to apply upward force

        if (height < heightLimit)
        {
            float heightFactor = Mathf.Clamp01((heightLimit - height) / heightLimit);
            rb.AddForce(Vector3.up * recoveryForce * heightFactor, ForceMode.Force);
        }
    }

    IEnumerator Charge()
    {
        isCharging = true;
        canCharge = false;

        // If in network mode, notify the network component about charging
        if (isNetworkMode && networkPlayer != null && networkPlayer.isLocalPlayer)
        {
            networkPlayer.CmdStartCharging();
        }

        // Play "baa" sound with pitch variation
        if (baaSound != null && audioSource != null)
        {
            // Configure audio source for baa sound
            audioSource.pitch = Random.Range(minPitch, maxPitch);
            audioSource.rolloffMode = AudioRolloffMode.Custom;
            audioSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff, baaRolloffCurve);
            audioSource.minDistance = baaMinDistance;
            audioSource.maxDistance = baaMaxDistance;

            // Play the sound with specified volume
            audioSource.PlayOneShot(baaSound, baaVolume);

            // Reset pitch after playing
            audioSource.pitch = 1.0f;
        }

        // Charging up phase
        currentChargeTime = 0f;
        while (currentChargeTime < chargeTime)
        {
            currentChargeTime += Time.deltaTime;
            // Could add visual indicator of charging here
            yield return null;
        }

        // Apply charge force
        rb.AddForce(transform.forward * chargePower, ForceMode.Impulse);

        isCharging = false;

        // Cooldown phase
        yield return new WaitForSeconds(chargeCooldown);
        canCharge = true;
    }

    // Method that can be called from NetworkSheepPlayer to perform charge
    public void NetworkCharge()
    {
        if (!isDead && !isCharging && !movementLocked)
        {
            rb.AddForce(transform.forward * chargePower, ForceMode.Impulse);
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (isDead || movementLocked) return;

        // Check if we hit another sheep with force
        if ((collision.gameObject.CompareTag("Sheep") || collision.gameObject.CompareTag("Player"))
            && rb.velocity.magnitude > 3f)
        {
            HandleSheepCollision(collision);
        }
    }

    // Extracted collision logic to be callable from network component
    public void HandleSheepCollision(Collision collision)
    {
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

            // Handle network case
            if (isNetworkMode && networkPlayer != null && networkPlayer.isLocalPlayer)
            {
                // Check if target is player or AI
                NetworkSheepPlayer otherNetworkPlayer = collision.gameObject.GetComponent<NetworkSheepPlayer>();
                if (otherNetworkPlayer != null)
                {
                    // Let the network component handle the hit notification
                    networkPlayer.HandleHitNetworkPlayer(otherNetworkPlayer.netId, finalForce);
                }

                NetworkAISheep otherNetworkAI = collision.gameObject.GetComponent<NetworkAISheep>();
                if (otherNetworkAI != null)
                {
                    // Let the network component handle AI hit notification
                    networkPlayer.HandleHitNetworkPlayer(otherNetworkAI.netId, finalForce);
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
        rb.maxAngularVelocity = 20f; // Increased from 12 to 20 for more dramatic knockdowns

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

        // For network mode, let the network component handle death
        if (isNetworkMode && networkPlayer != null && networkPlayer.isLocalPlayer)
        {
            networkPlayer.OnPlayerDeath();
        }
        else if (!isNetworkMode)
        {
            // Regular offline notification
            if (gameManager != null)
            {
                gameManager.PlayerDied();
            }

            // Play death effects
            PlayDeathEffects();
        }

        // Disable mesh renderer but keep collider to prevent further interactions
        MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>();
        foreach (MeshRenderer renderer in renderers)
        {
            renderer.enabled = false;
        }

        // Optionally disable this script after a short delay
        Invoke("DisableController", 1f);
    }

    // Play death effects without triggering network messages (can be called separately)
    public void PlayDeathEffects()
    {
        // Play explosion sound
        if (explosionSound != null && audioSource != null)
        {
            // Configure audio source for explosion sound
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

    void DisableController()
    {
        this.enabled = false;
    }
}