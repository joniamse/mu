namespace MuCustom.PlugIns.Vip;

using System.Collections.Concurrent;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Offline;
using MUnique.OpenMU.GameLogic.Views;
using MUnique.OpenMU.Interfaces;

/// <summary>
/// Tells the clients the VIP level of each character in the game server, so they draw its aura
/// (the client's <c>GameLogic/Vip/VipAura</c>): a notice <c>#MUAURA|name|level</c>, which the
/// modern client doesn't show. Level 0 is no aura; 1 to 4 are the VIP levels, and
/// <see cref="TopPvpLevel"/> is the aura of the weekly top 1 of the PvP ranking.
/// </summary>
public static class AuraService
{
    /// <summary>
    /// The aura of the weekly top 1 of the PvP ranking (not a VIP level).
    /// </summary>
    public const int TopPvpLevel = 5;

    private const string Prefix = "#MUAURA|";

    // The level last sent for each player.
    private static readonly ConcurrentDictionary<Player, int> Sent = new();

    // /aura: a level shown instead of the real one, to try the looks (test mode only).
    private static readonly ConcurrentDictionary<Player, int> Previews = new();

    /// <summary>
    /// Gets the aura level of a player: its VIP level, or the one it previews.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <returns>The level, 0 to 4.</returns>
    public static int GetLevel(Player player)
        => Previews.TryGetValue(player, out var preview) ? preview : VipService.GetLevel(player).Level;

    /// <summary>
    /// When a player entered the world: it learns the auras of the others, and they learn its.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <returns>The task.</returns>
    public static async ValueTask OnEnteredWorldAsync(Player player)
    {
        foreach (var other in await GetPlayersAsync(player.GameContext).ConfigureAwait(false))
        {
            if (other != player && GetLevel(other) is var level and > 0 && other.SelectedCharacter is { } character)
            {
                await SendAsync(player, character.Name, level).ConfigureAwait(false);
            }
        }

        Sent.TryRemove(player, out _);
        await BroadcastIfChangedAsync(player).ConfigureAwait(false);
    }

    /// <summary>
    /// Tells everybody the player's aura if it changed, e.g. after a purchase or an expiry.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <returns>The task.</returns>
    public static async ValueTask BroadcastIfChangedAsync(Player player)
    {
        if (player.SelectedCharacter is not { } character)
        {
            return;
        }

        var level = GetLevel(player);
        if (Sent.TryGetValue(player, out var sent) && sent == level)
        {
            return;
        }

        Sent[player] = level;
        foreach (var other in await GetPlayersAsync(player.GameContext).ConfigureAwait(false))
        {
            await SendAsync(other, character.Name, level).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Shows another level's aura on the player (<c>/aura</c>), or its own again with <c>null</c>.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="level">The level to show; <c>null</c> for the real one.</param>
    /// <returns>The task.</returns>
    public static ValueTask PreviewAsync(Player player, int? level)
    {
        if (level is { } shown)
        {
            Previews[player] = Math.Clamp(shown, 0, TopPvpLevel);
        }
        else
        {
            Previews.TryRemove(player, out _);
        }

        return BroadcastIfChangedAsync(player);
    }

    /// <summary>
    /// Forgets the player, when it left the world.
    /// </summary>
    /// <param name="player">The player.</param>
    public static void Forget(Player player)
    {
        Sent.TryRemove(player, out _);
        Previews.TryRemove(player, out _);
    }

    private static async ValueTask<IEnumerable<Player>> GetPlayersAsync(IGameContext gameContext)
    {
        var players = await gameContext.GetPlayersAsync().ConfigureAwait(false);
        return players.Where(p => p is not OfflinePlayer && p.PlayerState.CurrentState == PlayerState.EnteredWorld);
    }

    private static ValueTask SendAsync(Player receiver, string name, int level)
        => receiver.InvokeViewPlugInAsync<IShowMessagePlugIn>(p => p.ShowMessageAsync($"{Prefix}{name}|{level}", MessageType.BlueNormal));
}
