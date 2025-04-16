using Mirror;
using UnityEngine;

[RequireComponent(typeof(PlayerController))]
public class NetworkSheepPlayer : NetworkBehaviour
{
    [Header("Network Synced Variables")]
    [SyncVar(hook = nameof(OnNameChanged))]
    public string playerName = "NetworkSheep";

    [SyncVar]
    public bool hasVoted = false;

    // Reference to the base player controller
    private PlayerController playerController;
    private Rigidbody rb;

    // Keep track of death state to avoid multiple death notifications
    [SyncVar]
    private bool isDead = false;

    void Awake()
    {
        playerController = GetComponent<PlayerController>();
        rb = GetComponent<Rigidbody>();

        // Set tag for both client and server
        gameObject.tag = "Sheep";
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        // If no name was set, generate a random one
        if (string.IsNullOrEmpty(playerName))
        {
            playerName = $"Sheep_{Random.Range(100, 999)}";
        }
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // Make sure PlayerController knows we're in network mode
        if (playerController != null)
        {
            playerController.isNetworkMode = true;
        }

        // Disable player controller on non-local clients
        if (!isLocalPlayer && playerController != null)
        {
            playerController.enabled = false;
        }
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        // Load the player name from player prefs if available
        if (PlayerPrefs.HasKey("PlayerName"))
        {
            CmdSetPlayerName(PlayerPrefs.GetString("PlayerName"));
        }
        else
        {
            CmdSetPlayerName($"Player_{Random.Range(100, 999)}");
        }
    }

    // Command to set the player's name on the server
    [Command]
    public void CmdSetPlayerName(string newName)
    {
        // Simple validation
        if (string.IsNullOrEmpty(newName) || newName.Length > 20)
            return;

        playerName = newName;
    }

    // Command to register a vote to start
    [Command]
    public void CmdVote()
    {
        if (!hasVoted)
        {
            hasVoted = true;

            // Notify the lobby manager of the vote
            NetworkLobbyManager lobbyManager = FindObjectOfType<NetworkLobbyManager>();
            if (lobbyManager != null)
            {
                lobbyManager.PlayerVoted();
            }
        }
    }

    // Command to start charging
    [Command]
    public void CmdStartCharging()
    {
        // Tell all clients that this player is charging
        RpcOnStartCharging();
    }

    // ClientRpc to sync charging across all clients
    [ClientRpc]
    void RpcOnStartCharging()
    {
        // Don't play sound again for local player (they already did)
        if (isLocalPlayer)
            return;

        // Make non-local players charge
        if (playerController != null)
        {
            playerController.NetworkCharge();

            // Play the baa sound for non-local players
            AudioSource audioSource = GetComponent<AudioSource>();
            if (audioSource != null && playerController.baaSound != null)
            {
                audioSource.pitch = Random.Range(playerController.minPitch, playerController.maxPitch);
                audioSource.PlayOneShot(playerController.baaSound, playerController.baaVolume);
                audioSource.pitch = 1.0f;
            }
        }
    }

    // Called when the player name changes
    void OnNameChanged(string oldName, string newName)
    {
        // Update name display or any UI elements
        Debug.Log($"Player name changed from {oldName} to {newName}");
    }

    // Handle collisions between network players
    public void HandleHitNetworkPlayer(uint targetNetId, float impactForce)
    {
        if (isLocalPlayer)
        {
            CmdHitPlayer(targetNetId, transform.position, impactForce);
        }
    }

    [Command]
    void CmdHitPlayer(uint targetNetId, Vector3 hitPosition, float impactForce)
    {
        if (!NetworkServer.spawned.ContainsKey(targetNetId))
            return;

        // Get the target NetworkIdentity
        NetworkIdentity targetIdentity = NetworkServer.spawned[targetNetId];
        if (targetIdentity == null)
            return;

        // Get the target's position
        GameObject targetObj = targetIdentity.gameObject;
        Vector3 targetPos = targetObj.transform.position;

        // Calculate hit direction
        Vector3 hitDirection = (targetPos - hitPosition).normalized;
        hitDirection.y = 0; // Keep on horizontal plane

        // Apply effect on the target
        NetworkSheepPlayer targetPlayer = targetObj.GetComponent<NetworkSheepPlayer>();
        if (targetPlayer != null)
        {
            targetPlayer.RpcApplyHitForce(hitDirection, impactForce);
        }
    }

    [ClientRpc]
    void RpcApplyHitForce(Vector3 direction, float force)
    {
        if (playerController != null)
        {
            playerController.ApplyNetworkHitForce(direction, force);
        }
    }

    // Called when the player dies, need to send to server
    public void OnPlayerDeath()
    {
        if (isLocalPlayer && !isDead)
        {
            CmdPlayerDied();
        }
    }

    [Command]
    void CmdPlayerDied()
    {
        // Avoid duplicate deaths
        if (isDead) return;
        isDead = true;

        // Notify game manager on server
        if (GameManager.Instance != null)
        {
            GameManager.Instance.SheepDied(gameObject);
        }

        // Notify all clients that this player died
        RpcOnPlayerDied();
    }

    [ClientRpc]
    void RpcOnPlayerDied()
    {
        // Update local UI to show this player is dead
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

        // Mark as dead
        isDead = true;
    }
}