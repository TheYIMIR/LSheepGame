using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

public class SmoothCameraFollow : MonoBehaviour
{
    [Header("Target Settings")]
    [Tooltip("Will be automatically found if left empty")]
    public Transform target; // The player to follow
    public Vector3 offset = new Vector3(0, 5, -7); // Default offset from player
    public bool lookAtTarget = true; // Should camera look at player?
    public float findTargetInterval = 0.5f; // How often to look for target if not found

    [Header("Follow Settings")]
    [Range(0.01f, 1f)]
    public float smoothSpeed = 0.125f; // How smoothly the camera follows (lower = smoother)
    [Range(0.01f, 1f)]
    public float rotationSmoothing = 0.1f; // How smoothly the camera rotates
    public float minDistance = 3f; // Minimum follow distance
    public float maxDistance = 10f; // Maximum follow distance

    [Header("Dynamic Effects")]
    public bool enableSpeedEffects = true; // Whether to use speed-based effects
    [Range(0, 1)]
    public float fovEffectStrength = 0.2f; // Strength of speed-based FOV changes
    public float baseFOV = 60f; // Default FOV
    public float maxFOVIncrease = 15f; // Maximum FOV increase at top speed

    [Header("Screen Shake")]
    public bool enableScreenShake = true; // Toggle for screen shake
    [Range(0, 1)]
    public float shakeThreshold = 0.6f; // Speed threshold for shake to start (as % of max speed)
    [Range(0, 1)]
    public float shakeIntensity = 0.2f; // Base shake intensity
    public float maxShakeIntensity = 0.5f; // Maximum shake at top speed
    public float shakeFrequency = 2.0f; // How rapidly the shake moves

    [Header("Advanced Settings")]
    public bool useOcclusion = true; // Whether to adjust camera if view is blocked
    public LayerMask occlusionLayers; // Layers that block camera view
    public float occlusionSmoothSpeed = 0.2f; // How fast camera adjusts when view blocked

    // Private variables
    private Vector3 desiredPosition;
    private Vector3 smoothedPosition;
    private float currentDistance;
    private Quaternion desiredRotation;
    private Quaternion smoothedRotation;
    private Vector3 originalLocalPosition; // For screen shake
    private Camera cam;
    private Vector3 playerVelocity = Vector3.zero;
    private float shakeTimer = 0f;
    private bool isShaking = false;
    private float findTargetTimer = 0f;
    private Vector3 initialOffset;
    private float playerMaxSpeed = 0f;
    private bool isNetworkGame = false;

    void Start()
    {
        // Get the camera component
        cam = GetComponent<Camera>();
        if (cam == null)
        {
            cam = GetComponent<Camera>();
            if (cam == null)
            {
                Debug.LogError("No camera component found on this object!");
            }
        }

        // Check if we're in a network game
        isNetworkGame = NetworkClient.active;

        // Try to find the target initially
        FindTarget();

        // Store initial values
        initialOffset = offset;
        originalLocalPosition = Vector3.zero; // Local position for shake

        // Set default FOV
        if (cam != null)
        {
            cam.fieldOfView = baseFOV;
        }
    }

    void Update()
    {
        // If target not found, try to find it periodically
        if (target == null)
        {
            findTargetTimer += Time.deltaTime;
            if (findTargetTimer >= findTargetInterval)
            {
                FindTarget();
                findTargetTimer = 0f;
            }
            return;
        }

        // Reset find target timer when we have a target
        findTargetTimer = 0f;
    }

    void LateUpdate()
    {
        if (target == null)
            return;

        // Get current player speed and max speed
        float normalizedSpeed = GetNormalizedSpeed();

        // Calculate the desired position directly in world space
        desiredPosition = target.position + (offset.x * target.right) + (offset.y * Vector3.up) + (offset.z * target.forward);

        // Handle occlusion if enabled
        if (useOcclusion)
        {
            HandleCameraOcclusion();
        }

        // Apply smooth following
        smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed);
        transform.position = smoothedPosition;

        // Look at target if enabled
        if (lookAtTarget)
        {
            desiredRotation = Quaternion.LookRotation(target.position - transform.position);
            smoothedRotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationSmoothing);
            transform.rotation = smoothedRotation;
        }

        // Apply dynamic effects based on speed
        if (enableSpeedEffects && cam != null && normalizedSpeed > 0.1f) // Only apply effects when moving
        {
            // FOV effect - increase FOV with speed
            float targetFOV = baseFOV + (maxFOVIncrease * normalizedSpeed * fovEffectStrength);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFOV, Time.deltaTime * 5f);

            // Screen shake effect
            if (enableScreenShake && normalizedSpeed > shakeThreshold)
            {
                // Calculate shake amount based on how much over threshold we are
                float shakeAmount = Mathf.Lerp(shakeIntensity, maxShakeIntensity,
                    (normalizedSpeed - shakeThreshold) / (1 - shakeThreshold));
                ApplyScreenShake(shakeAmount);
            }
            else if (isShaking)
            {
                StopScreenShake();
            }
        }
        else if (cam != null && normalizedSpeed <= 0.1f)
        {
            // When nearly stopped, smoothly return to base FOV
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, baseFOV, Time.deltaTime * 3f);
            if (isShaking)
            {
                StopScreenShake();
            }
        }
    }

    // Get normalized speed (0-1) based on player's current and max speed
    float GetNormalizedSpeed()
    {
        // Get player rigidbody and controller
        Rigidbody rb = target.GetComponent<Rigidbody>();
        PlayerController controller = target.GetComponent<PlayerController>();

        if (rb != null)
        {
            // Get current velocity magnitude
            playerVelocity = rb.velocity;
            float currentSpeed = new Vector3(playerVelocity.x, 0, playerVelocity.z).magnitude; // Ignore Y for ground speed

            // Get max speed from player controller if available
            if (controller != null)
            {
                playerMaxSpeed = controller.maxSpeed;
            }
            else if (playerMaxSpeed <= 0)
            {
                // Fallback: estimate max speed if not yet obtained
                playerMaxSpeed = 10f;

                // Try to find it from AIPlayerController if applicable
                AIPlayerController aiController = target.GetComponent<AIPlayerController>();
                if (aiController != null)
                {
                    playerMaxSpeed = aiController.maxSpeed;
                }
            }

            // Return normalized speed
            if (playerMaxSpeed > 0)
            {
                return Mathf.Clamp01(currentSpeed / playerMaxSpeed);
            }
        }

        return 0f;
    }

    // Find the player in the scene - updated to prioritize local player in network games
    void FindTarget()
    {
        GameObject playerObject = null;

        // If in network mode, find the local player
        if (isNetworkGame)
        {
            playerObject = FindLocalNetworkPlayer();
        }

        // If not in network mode or if local player not found yet, use standard methods
        if (playerObject == null)
        {
            // First try to find by tag
            playerObject = GameObject.FindWithTag("Player");

            // If not found by tag, look for PlayerController component
            if (playerObject == null)
            {
                PlayerController[] playerControllers = FindObjectsOfType<PlayerController>();
                if (playerControllers.Length > 0)
                {
                    playerObject = playerControllers[0].gameObject;
                    Debug.Log("Found player via PlayerController component");
                }
            }

            // Try to find by AIPlayerController if still not found (for spectating AI)
            if (playerObject == null)
            {
                AIPlayerController[] aiControllers = FindObjectsOfType<AIPlayerController>();
                if (aiControllers.Length > 0)
                {
                    // Prefer any still-active AIs
                    foreach (AIPlayerController ai in aiControllers)
                    {
                        if (ai.gameObject.activeSelf)
                        {
                            playerObject = ai.gameObject;
                            Debug.Log("Found AI target: " + ai.name);
                            break;
                        }
                    }

                    // If none found active, take the first one
                    if (playerObject == null && aiControllers.Length > 0)
                    {
                        playerObject = aiControllers[0].gameObject;
                    }
                }
            }

            // Last resort - look for objects named "Player"
            if (playerObject == null)
            {
                playerObject = GameObject.Find("Player");

                if (playerObject == null)
                {
                    GameObject[] allObjects = FindObjectsOfType<GameObject>();
                    foreach (GameObject obj in allObjects)
                    {
                        if (obj.name.Contains("Player") || obj.GetComponent<PlayerController>() != null)
                        {
                            playerObject = obj;
                            Debug.Log("Found player via name: " + obj.name);
                            break;
                        }
                    }
                }
            }
        }

        // If found, assign it
        if (playerObject != null)
        {
            target = playerObject.transform;
            Debug.Log("Camera target assigned: " + target.name);

            // Get max speed immediately if possible
            PlayerController controller = target.GetComponent<PlayerController>();
            if (controller != null)
            {
                playerMaxSpeed = controller.maxSpeed;
                Debug.Log("Max speed from player: " + playerMaxSpeed);
            }
            else
            {
                AIPlayerController aiController = target.GetComponent<AIPlayerController>();
                if (aiController != null)
                {
                    playerMaxSpeed = aiController.maxSpeed;
                    Debug.Log("Max speed from AI: " + playerMaxSpeed);
                }
            }

            // Jump camera to initial position
            desiredPosition = target.position + (offset.x * target.right) + (offset.y * Vector3.up) + (offset.z * target.forward);
            transform.position = desiredPosition;

            if (lookAtTarget)
            {
                transform.LookAt(target);
            }
        }
        else
        {
            Debug.LogWarning("Camera couldn't find a player target!");
        }
    }

    // Find the local network player (if in network mode)
    GameObject FindLocalNetworkPlayer()
    {
        // Check for NetworkClient connection and local player
        if (NetworkClient.active && NetworkClient.localPlayer != null)
        {
            Debug.Log("Found local network player: " + NetworkClient.localPlayer.gameObject.name);
            return NetworkClient.localPlayer.gameObject;
        }

        // If that didn't work, try to find any NetworkSheepPlayer with isLocalPlayer=true
        NetworkSheepPlayer[] networkPlayers = FindObjectsOfType<NetworkSheepPlayer>();
        foreach (NetworkSheepPlayer player in networkPlayers)
        {
            if (player.isLocalPlayer)
            {
                Debug.Log("Found local network player via NetworkSheepPlayer: " + player.gameObject.name);
                return player.gameObject;
            }
        }

        Debug.Log("No local network player found, fallback to standard methods");
        return null;
    }

    void HandleCameraOcclusion()
    {
        // Cast a ray from target to desired camera position
        RaycastHit hit;
        Vector3 directionToCamera = (desiredPosition - target.position).normalized;
        float distanceToCamera = Vector3.Distance(target.position, desiredPosition);

        // If something is blocking the view
        if (Physics.Raycast(target.position, directionToCamera, out hit, distanceToCamera, occlusionLayers))
        {
            // Adjust distance to be just in front of the hit point
            float adjustedDistance = hit.distance * 0.9f; // 90% of the hit distance

            // Make sure it's not too close
            adjustedDistance = Mathf.Max(adjustedDistance, minDistance);

            // Set the new desired position
            desiredPosition = target.position + directionToCamera * adjustedDistance;
        }
    }

    void ApplyScreenShake(float intensity)
    {
        isShaking = true;
        shakeTimer += Time.deltaTime * shakeFrequency;

        // Create perlin noise-based shake
        float xShake = Mathf.PerlinNoise(shakeTimer, 0) * 2 - 1;
        float yShake = Mathf.PerlinNoise(0, shakeTimer) * 2 - 1;

        // Apply shake offset (to world position)
        Vector3 shakeOffset = new Vector3(xShake, yShake, 0) * intensity;
        transform.position += shakeOffset;
    }

    void StopScreenShake()
    {
        isShaking = false;
    }

    // Public methods to adjust camera settings at runtime

    public void SetOffset(Vector3 newOffset)
    {
        offset = newOffset;
    }

    public void SetDistance(float distance)
    {
        // Clamp the distance
        distance = Mathf.Clamp(distance, minDistance, maxDistance);

        // Calculate the new Z offset while maintaining X and Y
        float currentDistance = -offset.z; // Assuming Z is negative and represents distance
        float ratio = distance / currentDistance;
        offset.z = -distance; // Negative because it's behind the player
    }

    public void SetHeight(float height)
    {
        offset.y = height;
    }

    // Method to add a temporary screen shake (can be called from other scripts)
    public void AddImpactShake(float impactForce)
    {
        if (!enableScreenShake)
            return;

        StartCoroutine(ImpactShakeCoroutine(impactForce));
    }

    private IEnumerator ImpactShakeCoroutine(float impactForce)
    {
        float duration = 0.3f;
        float elapsed = 0f;
        float intensity = impactForce * shakeIntensity;

        while (elapsed < duration)
        {
            float xShake = Random.Range(-1f, 1f) * intensity;
            float yShake = Random.Range(-1f, 1f) * intensity;

            Vector3 shakeOffset = new Vector3(xShake, yShake, 0);
            transform.position += shakeOffset;

            elapsed += Time.deltaTime;
            intensity = Mathf.Lerp(impactForce * shakeIntensity, 0, elapsed / duration);

            yield return null;
        }
    }
}