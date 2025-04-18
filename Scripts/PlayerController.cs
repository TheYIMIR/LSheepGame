using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

/// <summary>
/// Controller for player-controlled sheep, handling input and player-specific behaviors.
/// Inherits from SheepController for common sheep physics and functionality.
/// </summary>
public class PlayerController : SheepController
{
    [Header("Player-Specific Settings")]
    public float chargeTime = 1.5f;
    public float recoveryForce = 5f;
    public float recoveryTorque = 3f;
    public float criticalAngle = 60f; // Angle in degrees considered "about to fall over"

    // Player state
    private bool isCharging = false;
    private bool canCharge = true;
    private float currentChargeTime = 0f;
    private bool isRecovering = false;

    // Network component reference
    private NetworkSheepPlayer networkPlayer;

    protected override void Awake()
    {
        base.Awake();

        // Try to get network component if it exists
        networkPlayer = GetComponent<NetworkSheepPlayer>();
    }

    protected override void Start()
    {
        base.Start();
    }

    void Update()
    {
        if (isDead || movementLocked) return;

        // In network mode, only process input for local player
        if (isNetworkMode && (networkPlayer == null || !networkPlayer.isLocalPlayer))
        {
            return;
        }

        // Handle rotation input
        float horizontalInput = Input.GetAxis("Horizontal");
        transform.Rotate(0, horizontalInput * rotationSpeed * Time.deltaTime, 0);

        // Handle charging
        if (Input.GetKeyDown(KeyCode.Space) && canCharge && !isCharging && !isRecovering)
        {
            StartCoroutine(Charge());
        }
    }

    protected override bool ShouldHandlePhysics()
    {
        // In network mode, only process physics for local player or on server
        if (isNetworkMode && networkPlayer != null)
        {
            if (!networkPlayer.isLocalPlayer && !NetworkServer.active)
            {
                return false;
            }
        }
        return true;
    }

    protected override bool ShouldApplyStrongStabilization()
    {
        // Don't apply strong stabilization during charging
        return !isCharging;
    }

    protected override void PerformStatePhysicsUpdate(float currentAngle)
    {
        // Check if we need recovery
        if (currentAngle > criticalAngle && currentAngle < deathAngle && !isCharging)
        {
            isRecovering = true;
            ApplyRecoveryForces();
        }
        else
        {
            isRecovering = false;
        }

        // Handle movement if not in recovery mode
        if (!isRecovering && !isCharging)
        {
            float verticalInput = 0;

            // In network mode, only get input for local player
            if (!isNetworkMode || (networkPlayer != null && networkPlayer.isLocalPlayer))
            {
                verticalInput = Input.GetAxis("Vertical");
            }

            // Only apply movement forces if somewhat upright
            float uprightness = Vector3.Dot(transform.up, Vector3.up);
            if (uprightness > 0.7f) // About 45 degrees or less from upright
            {
                // Calculate movement force
                Vector3 movementForce = transform.forward * verticalInput * accelerationForce;

                // Apply force for acceleration-based movement
                rb.AddForce(movementForce, ForceMode.Acceleration);

                // Add stabilizing downward force while moving
                if (verticalInput != 0)
                {
                    rb.AddForce(Vector3.down * accelerationForce * 1.2f, ForceMode.Force);
                }
            }
            else if (uprightness > 0.4f) // Partially tipped but not in full recovery
            {
                // Apply reduced movement if partially tipped
                float verticalInputReduced = verticalInput * 0.5f;
                Vector3 movementForce = transform.forward * verticalInputReduced * accelerationForce;
                rb.AddForce(movementForce, ForceMode.Acceleration);
            }

            // Dampen rotation around forward axis to prevent barrel rolling
            if (Mathf.Abs(verticalInput) > 0.1f)
            {
                Vector3 angVel = rb.angularVelocity;
                Vector3 forwardDirection = transform.forward;
                float forwardAngVel = Vector3.Dot(angVel, forwardDirection);
                rb.angularVelocity -= forwardDirection * forwardAngVel * 0.9f * Time.deltaTime;
            }
        }

        // Apply anti-spin when not recently hit
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

        if (!isRecovering && !isCharging)
        {
            // Apply additional stabilization for smooth high-speed movement
            ApplyAdditionalStabilizationAtSpeed(currentAngle);
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
                // Find what type of network sheep we hit
                NetworkSheepController targetNetwork = collision.gameObject.GetComponent<NetworkSheepController>();
                if (targetNetwork != null)
                {
                    // Let the network component handle the hit notification
                    networkPlayer.SyncHitNetworkEntity(targetNetwork.netId, impactDirection.normalized, finalForce);
                }
            }
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

        // Disable colliders to prevent further collisions
        Collider[] colliders = GetComponentsInChildren<Collider>();
        foreach (Collider collider in colliders)
        {
            collider.enabled = false;
        }

        // Disable rigidbody to prevent further physics interactions
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.detectCollisions = false;
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
}