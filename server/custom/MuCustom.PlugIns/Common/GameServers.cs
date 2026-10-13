namespace MuCustom.PlugIns.Common;

using System.Collections.Concurrent;
using MUnique.OpenMU.GameLogic;

/// <summary>
/// The game servers (game contexts) of this process. They register through the periodic task and
/// whenever a feature sees one, so features can reach all of them: e.g. announcements, or finding
/// an offline store of an account on another game server.
/// </summary>
public static class GameServers
{
    private static readonly ConcurrentDictionary<IGameContext, byte> Contexts = new();

    /// <summary>
    /// Gets the known game contexts.
    /// </summary>
    public static ICollection<IGameContext> All => Contexts.Keys;

    /// <summary>
    /// Registers a game context.
    /// </summary>
    /// <param name="gameContext">The game context.</param>
    public static void Register(IGameContext gameContext) => Contexts.TryAdd(gameContext, 0);

    /// <summary>
    /// Gets the id of the game server, or -1 if it's not a game server.
    /// </summary>
    /// <param name="gameContext">The game context.</param>
    /// <returns>The id.</returns>
    public static int GetId(IGameContext gameContext) => gameContext is IGameServerContext server ? server.Id : -1;
}
