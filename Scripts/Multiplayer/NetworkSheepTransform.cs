using Mirror;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NetworkSheepTransform : NetworkBehaviour
{
    // Sync position and rotation at a reasonable update rate
    [SyncVar]
    private Vector3 syncedPosition;

    [SyncVar]
    private Quaternion syncedRotation;

    // Components
    private Rigidbody rb;
    private Transform sheepTransform;

    // Settings
    [Header("Sync Settings")]
    public float positionThreshold = 0.1f; // Only sync when moved this distance
    public float rotationThreshold = 2f; // Only sync when rotated this many degrees
    public float syncInterval = 0.1f; // Sync rate in seconds

    // Private variables
    private float lastSyncTime = 0f;
    private Vector3 lastSyncedPosition;
    private Quaternion lastSyncedRotation;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        sheepTransform = transform;
    }

    void Start()
    {
        // Initialize sync values
        if (isServer)
        {
            syncedPosition = sheepTransform.position;
            syncedRotation = sheepTransform.rotation;
            lastSyncedPosition = syncedPosition;
            lastSyncedRotation = syncedRotation;
        }
    }

    void Update()
    {
        // Server updates the synced values
        if (isServer)
        {
            // Only sync at specified interval
            if (Time.time > lastSyncTime + syncInterval)
            {
                // Only sync if there's been significant movement
                float positionDelta = Vector3.Distance(sheepTransform.position, lastSyncedPosition);
                float rotationDelta = Quaternion.Angle(sheepTransform.rotation, lastSyncedRotation);

                if (positionDelta > positionThreshold || rotationDelta > rotationThreshold)
                {
                    syncedPosition = sheepTransform.position;
                    syncedRotation = sheepTransform.rotation;
                    lastSyncedPosition = syncedPosition;
                    lastSyncedRotation = syncedRotation;
                    lastSyncTime = Time.time;
                }
            }
        }
        // Clients update their positions based on server values
        else if (!isLocalPlayer) // Don't override local player movement
        {
            // Interpolate to smooth movement
            sheepTransform.position = Vector3.Lerp(sheepTransform.position, syncedPosition, Time.deltaTime * 10f);
            sheepTransform.rotation = Quaternion.Slerp(sheepTransform.rotation, syncedRotation, Time.deltaTime * 10f);

            // Make sure rigidbody doesn't override network movement
            if (rb != null)
            {
                rb.isKinematic = true;
            }
        }
    }
}
