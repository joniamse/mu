namespace MuCustom.PlugIns.Vip;

/// <summary>
/// To whom a membership applies.
/// </summary>
public enum VipScope
{
    /// <summary>The character which bought it.</summary>
    Character,

    /// <summary>All characters of the account.</summary>
    Account,
}

/// <summary>
/// A VIP membership level and its benefits.
/// </summary>
/// <param name="Level">The level, 0 (free) to 4.</param>
/// <param name="Name">The display name.</param>
/// <param name="CostFichas">The price for <see cref="VipLevels.Duration"/>.</param>
/// <param name="Scope">To whom it applies.</param>
/// <param name="ExperienceBonus">The experience bonus, e.g. 0.1 for +10 %.</param>
/// <param name="DropBonus">The item drop chance bonus, e.g. 0.05 for +5 %.</param>
/// <param name="ChaosBonus">Percentage points added to the chaos machine success rate.</param>
/// <param name="Vaults">The number of vaults (<c>/baul</c>).</param>
/// <param name="OffStore">How long an offline store stays open; <see cref="TimeSpan.Zero"/> for none, <c>null</c> for unlimited.</param>
/// <param name="OffStoreWhilePlaying">If the offline store stays open when the account plays another character.</param>
/// <param name="HelperDiscount">The discount on the MU helper zen cost, e.g. 0.3 for -30 %.</param>
/// <param name="DailyEventEntries">The free Blood Castle and Devil Square entries per day and character.</param>
public sealed record VipLevel(
    int Level,
    string Name,
    int CostFichas,
    VipScope Scope,
    double ExperienceBonus,
    double DropBonus,
    double ChaosBonus,
    int Vaults,
    TimeSpan? OffStore,
    bool OffStoreWhilePlaying,
    double HelperDiscount,
    int DailyEventEntries)
{
    /// <summary>
    /// Gets a value indicating whether the level allows an offline store.
    /// </summary>
    public bool HasOffStore => this.OffStore != TimeSpan.Zero;
}

/// <summary>
/// The VIP levels.
/// </summary>
public static class VipLevels
{
    /// <summary>
    /// How long a bought membership lasts.
    /// </summary>
    public static readonly TimeSpan Duration = TimeSpan.FromDays(30);

    /// <summary>
    /// The levels, indexed by their number.
    /// </summary>
    public static readonly IReadOnlyList<VipLevel> All =
    [
        new(0, "Free", 0, VipScope.Character, 0, 0, 0, 1, TimeSpan.Zero, false, 0, 0),
        new(1, "Plata", 500, VipScope.Character, 0.10, 0.05, 3, 2, TimeSpan.FromHours(12), false, 0.30, 1),
        new(2, "Oro", 1000, VipScope.Character, 0.20, 0.10, 5, 3, TimeSpan.FromHours(24), false, 0.50, 2),
        new(3, "Platino", 1500, VipScope.Character, 0.30, 0.15, 10, 5, null, false, 1.0, 3),
        new(4, "Diamante", 2500, VipScope.Account, 0.30, 0.15, 10, 5, null, true, 1.0, 3),
    ];

    /// <summary>
    /// Gets the free level.
    /// </summary>
    public static VipLevel Free => All[0];

    /// <summary>
    /// Gets the level with the given number, or the free level.
    /// </summary>
    /// <param name="level">The level number.</param>
    /// <returns>The level.</returns>
    public static VipLevel Get(int level) => level >= 0 && level < All.Count ? All[level] : Free;
}
