using UnityEngine.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Mirror;

public class MainMenuUI : MonoBehaviour
{
    [Header("UI References")]
    public Button singlePlayerButton;
    public Button multiplayerButton;
    public Button quitButton;

    [Header("Network Settings")]
    public SheepNetworkManager networkManager;

    private void Start()
    {
        // Initialize network manager reference if not set
        if (networkManager == null)
        {
            networkManager = FindObjectOfType<SheepNetworkManager>();
        }

        // Register button events
        singlePlayerButton.onClick.AddListener(OnSinglePlayerClicked);
        multiplayerButton.onClick.AddListener(OnMultiplayerClicked);
        quitButton.onClick.AddListener(OnQuitClicked);
    }

    public void OnSinglePlayerClicked()
    {
        // Load the regular single player scene
        SceneManager.LoadScene("SheepBattleground");
    }

    public void OnMultiplayerClicked()
    {
        // Load lobby scene for multiplayer
        SceneManager.LoadScene(NetworkGameConfig.LOBBY_SCENE_NAME);
    }

    public void OnQuitClicked()
    {
        // Quit application
        Application.Quit();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}