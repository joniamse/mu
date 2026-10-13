namespace MuCustom.PlugIns.Vip;

/// <summary>
/// The memberships of a character and its account.
/// </summary>
/// <param name="AccountLevel">The level of the account membership (Diamante).</param>
/// <param name="AccountExpiresAt">When the account membership expires.</param>
/// <param name="CharacterLevel">The level of the character membership.</param>
/// <param name="CharacterExpiresAt">When the character membership expires.</param>
public sealed record VipStatus(int AccountLevel, DateTime? AccountExpiresAt, int CharacterLevel, DateTime? CharacterExpiresAt)
{
    /// <summary>
    /// Gets the status without membership.
    /// </summary>
    public static VipStatus None { get; } = new(0, null, 0, null);

    /// <summary>
    /// Gets the membership which applies now: the higher one of the active memberships.
    /// </summary>
    /// <param name="nowUtc">The current time.</param>
    /// <returns>The level and its expiration.</returns>
    public (VipLevel Level, DateTime? ExpiresAt) GetEffective(DateTime nowUtc)
    {
        var account = this.AccountExpiresAt > nowUtc ? this.AccountLevel : 0;
        var character = this.CharacterExpiresAt > nowUtc ? this.CharacterLevel : 0;
        if (account == 0 && character == 0)
        {
            return (VipLevels.Free, null);
        }

        return account >= character
            ? (VipLevels.Get(account), this.AccountExpiresAt)
            : (VipLevels.Get(character), this.CharacterExpiresAt);
    }
}
