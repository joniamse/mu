namespace MuCustom.PlugIns.Vip;

/// <summary>
/// The outcome of a membership purchase.
/// </summary>
public enum VipPurchaseOutcome
{
    /// <summary>Bought; it starts now.</summary>
    Purchased,

    /// <summary>The same level was active and got extended.</summary>
    Renewed,

    /// <summary>Not enough fichas.</summary>
    InsufficientFichas,

    /// <summary>A higher level is active, buying this one would be a waste.</summary>
    HigherLevelActive,

    /// <summary>The level does not exist or can not be bought.</summary>
    InvalidLevel,
}

/// <summary>
/// The result of a membership purchase.
/// </summary>
/// <param name="Outcome">The outcome.</param>
/// <param name="Balance">The fichas balance afterwards.</param>
/// <param name="Status">The memberships afterwards.</param>
public sealed record VipPurchaseResult(VipPurchaseOutcome Outcome, long Balance, VipStatus Status);
