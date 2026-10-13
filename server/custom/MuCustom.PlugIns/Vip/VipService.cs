namespace MuCustom.PlugIns.Vip;

using System.Runtime.CompilerServices;
using MuCustom.PlugIns.Data;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Attributes;

/// <summary>
/// Keeps the memberships of the online characters in memory, because the rates are needed
/// synchronously and often (every kill).
/// </summary>
/// <remarks>
/// The status is loaded when a character enters the world, after a purchase, and refreshed
/// periodically. The expiration is checked on every read, so an expired membership stops
/// applying at once even without a refresh.
/// The level is also published as the OpenMU attribute <see cref="Stats.IsVip"/>, which some
/// built-in commands (e.g. <c>/openware</c>) can require.
/// </remarks>
public static class VipService
{
    private static readonly ConditionalWeakTable<Player, Entry> Entries = new();

    /// <summary>
    /// Gets the membership which applies to the player now.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <returns>The level and its expiration.</returns>
    public static (VipLevel Level, DateTime? ExpiresAt) GetEffective(Player player)
    {
        return GetStatus(player).GetEffective(DateTime.UtcNow);
    }

    /// <summary>
    /// Gets the level which applies to the player now.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <returns>The level.</returns>
    public static VipLevel GetLevel(Player player) => GetEffective(player).Level;

    /// <summary>
    /// Gets the cached status of the player's current character.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <returns>The status.</returns>
    public static VipStatus GetStatus(Player player)
    {
        return Entries.TryGetValue(player, out var entry) && entry.CharacterId == player.SelectedCharacter?.GetId()
            ? entry.Status
            : VipStatus.None;
    }

    /// <summary>
    /// Loads the status of the player from the database.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <returns>The status.</returns>
    public static async ValueTask<VipStatus> RefreshAsync(Player player)
    {
        if (player.Account is not { } account || player.SelectedCharacter is not { } character || !CustomDatabase.IsReady)
        {
            return VipStatus.None;
        }

        var status = await VipRepository.GetStatusAsync(account.GetId(), character.GetId()).ConfigureAwait(false);
        Set(player, status);
        return status;
    }

    /// <summary>
    /// Sets the status of the player's current character, e.g. after a purchase.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="status">The status.</param>
    public static void Set(Player player, VipStatus status)
    {
        if (player.SelectedCharacter is not { } character)
        {
            return;
        }

        var entry = Entries.GetValue(player, _ => new Entry());
        lock (entry)
        {
            entry.CharacterId = character.GetId();
            entry.Status = status;
        }

        UpdateVipAttribute(player);
    }

    /// <summary>
    /// Updates the <see cref="Stats.IsVip"/> attribute of the player, e.g. after the membership expired.
    /// </summary>
    /// <param name="player">The player.</param>
    public static void UpdateVipAttribute(Player player)
    {
        if (player.Attributes is not { } attributes || !Entries.TryGetValue(player, out var entry))
        {
            return;
        }

        var level = GetLevel(player).Level;
        lock (entry)
        {
            // The attribute system is created again for every character selection.
            if (entry.Element is null || !ReferenceEquals(entry.Attributes, attributes))
            {
                entry.Element = new SimpleElement(level, AggregateType.AddRaw);
                entry.Attributes = attributes;
                attributes.AddElement(entry.Element, Stats.IsVip);
            }
            else if ((int)entry.Element.Value != level)
            {
                entry.Element.Value = level;
            }
        }
    }

    /// <summary>
    /// Forgets the status, when the player left the world.
    /// </summary>
    /// <param name="player">The player.</param>
    public static void Forget(Player player)
    {
        if (!Entries.TryGetValue(player, out var entry))
        {
            return;
        }

        lock (entry)
        {
            if (entry.Element is not null && entry.Attributes is { } attributes)
            {
                attributes.RemoveElement(entry.Element, Stats.IsVip);
            }
        }

        Entries.Remove(player);
    }

    private sealed class Entry
    {
        public Guid CharacterId { get; set; }

        public VipStatus Status { get; set; } = VipStatus.None;

        public SimpleElement? Element { get; set; }

        public AttributeSystem? Attributes { get; set; }
    }
}
