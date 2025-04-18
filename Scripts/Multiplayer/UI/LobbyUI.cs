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

    [Header("Additional UI Elements")]
    public Text statusText;
    public Toggle botToggle;
    public Slider botCountSlider;
    public Text botCountText;

    [Header("Network References")]
    public SheepNetworkManager networkManager;

    // Game settings
    [Tooltip("If true, only players count for last man standing (ignores AI)")]
    public bool playersOnlyMode = false;

    private string playerPrefsNameKey = "PlayerName";
    private NetworkLobbyManager lobbyManager;

    private void Start()
    {
        // Find network manager if not set
        if (networkManager == null)
        {
            networkManager = FindObjectOfType<SheepNetworkManager>();
        }

        // Find lobby manager
        lobbyManager = FindObjectOfType<NetworkLobbyManager>();

        // Setup button listeners
        hostButton.onClick.AddListener(OnHostClicked);
        joinButton.onClick.AddListener(OnJoinClicked);
        backButton.onClick.AddListener(OnBackClicked);
        voteButton.onClick.AddListener(OnVoteClicked);

        // Setup additional UI if available
        if (botToggle != null)
        {
            botToggle.onValueChanged.AddListener(OnBotToggleChanged);
        }

        if (botCountSlider != null)
        {
            botCountSlider.onValueChanged.AddListener(OnBotCountChanged);
            botCountSlider.value = NetworkGameConfig.DEFAULT_BOT_COUNT_SINGLE_PLAYER;
        }

        // Initialize UI states
        if (voteButton != null)
        {
            voteButton.interactable = false; // Enabled once connected
        }

        // Load saved player name
        if (PlayerPrefs.HasKey(playerPrefsNameKey))
        {
            playerNameInput.text = PlayerPrefs.GetString(playerPrefsNameKey);
        }
        else
        {
            // Generate random default name
            playerNameInput.text = $"Sheep_{Random.Range(100, 9999)}";
        }

        // Disable main menu UI if already connected (scene reload)
        if (NetworkClient.active)
        {
            DisableMainMenuUI();
            EnableLobbyUI();
        }
    }

    void Update()
    {
        // Check connection status
        if (NetworkClient.active)
        {
            // Enable vote button once connected
            if (voteButton != null)
            {
                voteButton.interactable = true;

                // Disable if already voted
                if (NetworkClient.connection != null && NetworkClient.connection.identity != null)
                {
                    NetworkSheepPlayer localPlayer = NetworkClient.connection.identity.GetComponent<NetworkSheepPlayer>();
                    if (localPlayer != null && localPlayer.hasVoted)
                    {
                        voteButton.interactable = false;
                    }
                }
            }
        }
    }

    // Toggle between main menu and lobby UI
    void DisableMainMenuUI()
    {
        if (hostButton != null) hostButton.gameObject.SetActive(false);
        if (joinButton != null) joinButton.gameObject.SetActive(false);
        if (ipAddressInput != null) ipAddressInput.gameObject.SetActive(false);
        if (playerNameInput != null) playerNameInput.gameObject.SetActive(false);
        if (botToggle != null) botToggle.gameObject.SetActive(false);
        if (botCountSlider != null) botCountSlider.gameObject.SetActive(false);
        if (botCountText != null) botCountText.gameObject.SetActive(false);
    }

    void EnableLobbyUI()
    {
        if (countdownText != null) countdownText.gameObject.SetActive(true);
        if (voteButton != null) voteButton.gameObject.SetActive(true);
        if (playersCountText != null) playersCountText.gameObject.SetActive(true);
        if (votesText != null) votesText.gameObject.SetActive(true);

        // Show status connected
        if (statusText != null)
        {
            statusText.text = NetworkServer.active ? "Hosting Game" : "Connected to Server";
            statusText.gameObject.SetActive(true);
        }
    }

    void OnVoteClicked()
    {
        // Tell the network manager to register this player's vote
        if (NetworkClient.active && NetworkClient.isConnected)
        {
            NetworkRoomPlayerSheep localPlayer = NetworkClient.connection.identity.GetComponent<NetworkRoomPlayerSheep>();
            if (localPlayer != null)
            {
                localPlayer.CmdVoteToStart();

                // Disable vote button
                voteButton.interactable = false;
            }
        }
    }

    void OnHostClicked()
    {
        SavePlayerName();

        // Save bot settings if available
        if (botToggle != null)
        {
            // Store the setting that will be passed to the NetworkGameManager when created
            playersOnlyMode = !botToggle.isOn;

            // Save the setting to PlayerPrefs for persistence
            PlayerPrefs.SetInt("PlayersOnlyMode", playersOnlyMode ? 1 : 0);
            PlayerPrefs.Save();
        }

        if (botCountSlider != null && networkManager != null)
        {
            // Set bot count in the NetworkManager
            networkManager.botCount = (int)botCountSlider.value;

            // Save to PlayerPrefs
            PlayerPrefs.SetInt("BotCount", (int)botCountSlider.value);
            PlayerPrefs.Save();
        }

        // Start hosting
        if (networkManager != null)
        {
            networkManager.StartHost();

            // Update UI
            DisableMainMenuUI();
            EnableLobbyUI();
        }
    }

    void OnJoinClicked()
    {
        SavePlayerName();

        string ipAddress = ipAddressInput.text;
        if (!string.IsNullOrEmpty(ipAddress))
        {
            if (networkManager != null)
            {
                networkManager.networkAddress = ipAddress;
                networkManager.StartClient();

                // Update UI
                DisableMainMenuUI();
                EnableLobbyUI();

                // Show connecting status
                if (statusText != null)
                {
                    statusText.text = "Connecting...";
                }
            }
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

    void OnBotToggleChanged(bool includeBotsInLastManStanding)
    {
        // Update bot count UI visibility
        if (botCountSlider != null)
        {
            botCountSlider.gameObject.SetActive(includeBotsInLastManStanding);
        }

        if (botCountText != null)
        {
            botCountText.gameObject.SetActive(includeBotsInLastManStanding);
        }

        // Update the local setting
        playersOnlyMode = !includeBotsInLastManStanding;
    }

    void OnBotCountChanged(float count)
    {
        if (botCountText != null)
        {
            botCountText.text = $"Bot Count: {(int)count}";
        }
    }

    void SavePlayerName()
    {
        if (!string.IsNullOrEmpty(playerNameInput.text))
        {
            PlayerPrefs.SetString(playerPrefsNameKey, playerNameInput.text);
            PlayerPrefs.Save();

            // Set player name in network manager
            if (networkManager != null)
            {
                networkManager.playerName = playerNameInput.text;
            }
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

    // Called when connection status changes
    public void OnConnectionStatusChanged(bool success, string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }

        // If connected, update UI
        if (success)
        {
            DisableMainMenuUI();
            EnableLobbyUI();
        }
    }
}