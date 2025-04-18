using UnityEngine;
using UnityEngine.UI;
using Mirror;

public class NetworkRoomPlayerSheep : NetworkRoomPlayer
{
    [Header("Player Info")]
    [SyncVar(hook = nameof(OnPlayerNameChanged))]
    public string playerName = "Sheep";

    [Header("UI References")]
    public Text playerNameText;
    public Text readyStatusText;

    // Called when the script is started
    public override void OnStartClient()
    {
        base.OnStartClient();
        UpdateUI();

        // Set player name if this is the local player
        if (isLocalPlayer && PlayerPrefs.HasKey("PlayerName"))
        {
            CmdSetPlayerName(PlayerPrefs.GetString("PlayerName"));
        }
    }

    // Called when readyToBegin changes
    public override void ReadyStateChanged(bool oldReadyState, bool newReadyState)
    {
        base.ReadyStateChanged(oldReadyState, newReadyState);
        UpdateUI();

        // Notify the lobby manager about ready state changes
        NetworkLobbyManager lobbyManager = FindObjectOfType<NetworkLobbyManager>();
        if (lobbyManager != null)
        {
            lobbyManager.CheckReadyToStart();
        }
    }

    // Called when the player enters the room
    public override void OnClientEnterRoom()
    {
        base.OnClientEnterRoom();
        UpdateUI();
    }

    // Called when the player leaves the room
    public override void OnClientExitRoom()
    {
        base.OnClientExitRoom();
    }

    // Update UI elements based on current state
    private void UpdateUI()
    {
        if (playerNameText != null)
        {
            playerNameText.text = playerName;
        }

        if (readyStatusText != null)
        {
            readyStatusText.text = readyToBegin ? "Ready" : "Not Ready";
            readyStatusText.color = readyToBegin ? Color.green : Color.red;
        }
    }

    // Called when player name changes
    void OnPlayerNameChanged(string oldName, string newName)
    {
        UpdateUI();
    }

    // Command to set player name
    [Command]
    public void CmdSetPlayerName(string newName)
    {
        if (string.IsNullOrEmpty(newName) || newName.Length > 20)
            return;

        playerName = newName;
    }
}