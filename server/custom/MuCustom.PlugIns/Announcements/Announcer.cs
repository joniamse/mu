namespace MuCustom.PlugIns.Announcements;

using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.Interfaces;

/// <summary>
/// Sends announcements to all players of all game servers of this process.
/// </summary>
/// <remarks>
/// OpenMU's "global" messages reach the players of one game server; this sends them to all the
/// game servers of <see cref="GameServers"/>. It is shown in the center of the screen and also
/// stays in the chat.
/// </remarks>
public static class Announcer
{
    /// <summary>
    /// Sends an announcement.
    /// </summary>
    /// <param name="origin">The game context where it happened; always included.</param>
    /// <param name="message">The message.</param>
    /// <returns>The task.</returns>
    public static async ValueTask AnnounceAsync(IGameContext origin, string message)
    {
        GameServers.Register(origin);
        foreach (var gameContext in GameServers.All)
        {
            await gameContext.SendGlobalMessageAsync(message, MessageType.GoldenCenter).ConfigureAwait(false);
            await gameContext.SendGlobalMessageAsync(message, MessageType.BlueNormal).ConfigureAwait(false);
        }
    }
}
