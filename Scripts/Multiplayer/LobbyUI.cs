using Mirror;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine;

public class LobbyUI : MonoBehaviour
{
    [Header("UI References")]
    public Text countdownText;
    public Button voteButton;
    public Text playersCountText;
    public Text votesText;
    public InputField playerNameInput;
    public Button hostButton;
    public Button joinButton;
    public InputField ipAddressInput;
    public Button backButton;

    [Header("Network References")]
    public SheepNetworkManager networkManager;

    private string playerPrefsNameKey = "PlayerName";

    private void Start()
    {
        // Find network manager if not set
        if (networkManager == null)
        {
            networkManager = FindObjectOfType<SheepNetworkManager>();
        }

        // Setup button listeners
        hostButton.onClick.AddListener(OnHostClicked);
        joinButton.onClick.AddListener(OnJoinClicked);
        backButton.onClick.AddListener(OnBackClicked);
        voteButton.onClick.AddListener(OnVoteClicked);

        // Load saved player name
        if (PlayerPrefs.HasKey(playerPrefsNameKey))
        {
            playerNameInput.text = PlayerPrefs.GetString(playerPrefsNameKey);
        }
        else
        {
            // Generate random default name
            playerNameInput.text = $"Sheep_{UnityEngine.Random.Range(100, 9999)}";
        }
    }

    void OnVoteClicked()
    {
        // Tell the network manager to register this player's vote
        if (NetworkClient.active && NetworkClient.isConnected)
        {
            NetworkSheepPlayer localPlayer = NetworkClient.connection.identity.GetComponent<NetworkSheepPlayer>();
            if (localPlayer != null)
            {
                localPlayer.CmdVote();
            }
        }
    }

    void OnHostClicked()
    {
        SavePlayerName();
        networkManager.StartHost();
    }

    void OnJoinClicked()
    {
        SavePlayerName();

        string ipAddress = ipAddressInput.text;
        if (!string.IsNullOrEmpty(ipAddress))
        {
            networkManager.networkAddress = ipAddress;
            networkManager.StartClient();
        }
    }

    void OnBackClicked()
    {
        // Stop any network connection
        if (NetworkClient.active)
        {
            NetworkManager.singleton.StopClient();
        }

        if (NetworkServer.active)
        {
            NetworkManager.singleton.StopServer();
        }

        // Go back to main menu
        SceneManager.LoadScene(NetworkGameConfig.MENU_SCENE_NAME);
    }

    void SavePlayerName()
    {
        if (!string.IsNullOrEmpty(playerNameInput.text))
        {
            PlayerPrefs.SetString(playerPrefsNameKey, playerNameInput.text);
            PlayerPrefs.Save();

            // Set player name in network manager
            networkManager.playerName = playerNameInput.text;
        }
    }

    // Update UI methods
    public void UpdateCountdownUI(float countdown)
    {
        if (countdownText != null)
        {
            countdownText.text = $"Game starts in: {Mathf.CeilToInt(countdown)}";
        }
    }

    public void UpdateVotesUI(int votes, int players)
    {
        if (votesText != null && players > 0)
        {
            votesText.text = $"Votes: {votes}/{players} ({Mathf.Floor((float)votes / players * 100)}%)";
        }
    }

    public void UpdatePlayersUI(int currentPlayers)
    {
        if (playersCountText != null)
        {
            playersCountText.text = $"Players: {currentPlayers}/{NetworkGameConfig.MAX_PLAYERS_PER_LOBBY}";
        }
    }
}