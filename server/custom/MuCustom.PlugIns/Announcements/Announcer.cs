namespace MuCustom.PlugIns.Announcements;

using System.Collections.Concurrent;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.Interfaces;

/// <summary>
/// Sends announcements to all players of all game servers of this process.
/// </summary>
/// <remarks>
/// OpenMU's "global" messages reach the players of one game server. The game servers of this
/// process register here (by the periodic task), so an announcement reaches all of them.
/// It is shown in the center of the screen and also stays in the chat.
/// </remarks>
public static class Announcer
{
    private static readonly ConcurrentDictionary<IGameContext, byte> GameContexts = new();

    /// <summary>
    /// Registers a game server.
    /// </summary>
    /// <param name="gameContext">The game context of the game server.</param>
    public static void Register(IGameContext gameContext) => GameContexts.TryAdd(gameContext, 0);

    /// <summary>
    /// Sends an announcement.
    /// </summary>
    /// <param name="origin">The game context where it happened; always included.</param>
    /// <param name="message">The message.</param>
    /// <returns>The task.</returns>
    public static async ValueTask AnnounceAsync(IGameContext origin, string message)
    {
        Register(origin);
        foreach (var gameContext in GameContexts.Keys)
        {
            await gameContext.SendGlobalMessageAsync(message, MessageType.GoldenCenter).ConfigureAwait(false);
            await gameContext.SendGlobalMessageAsync(message, MessageType.BlueNormal).ConfigureAwait(false);
        }
    }
}
