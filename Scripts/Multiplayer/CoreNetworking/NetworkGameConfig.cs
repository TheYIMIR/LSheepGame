public static class NetworkGameConfig
{
    // Player limits
    public const int MAX_PLAYERS_PER_LOBBY = 100;

    // Timing settings
    public const float LOBBY_COUNTDOWN_SECONDS = 60f;
    public const float VOTE_SKIP_PERCENTAGE = 0.5f;
    public const float VOTE_SKIP_COUNTDOWN_SECONDS = 3f;

    // Scene names
    public const string GAME_SCENE_NAME = "SheepBattleground";
    public const string LOBBY_SCENE_NAME = "Lobby";
    public const string MENU_SCENE_NAME = "MainMenu";

    // Network performance settings
    public const float SYNC_RATE = 0.2f; // How often to sync transform data (5 times per second)
    public const float POSITION_THRESHOLD = 0.1f; // Only sync position changes larger than this
    public const float ROTATION_THRESHOLD = 2.0f; // Only sync rotation changes larger than this (degrees)

    // Gameplay settings
    public const bool DEFAULT_LAST_MAN_STANDING_PLAYERS_ONLY = false; // If true, only players count for victory
    public const int DEFAULT_BOT_COUNT_SINGLE_PLAYER = 15; // Default bots for single player
    public const int MAX_BOT_COUNT = 50; // Maximum bots allowed
}