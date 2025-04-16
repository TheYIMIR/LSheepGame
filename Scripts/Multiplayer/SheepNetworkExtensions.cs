using Mirror;

public static class SheepNetworkExtensions
{
    // Extension method to set player name
    [Command]
    public static void CmdSetName(this NetworkSheepPlayer player, string newName)
    {
        player.playerName = newName;
    }
}