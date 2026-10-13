namespace MuCustom.PlugIns.OffStore;

using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Offline;

/// <summary>
/// The character of an offline store (<c>/offstore</c>): it stays in the world with its personal
/// store open while the player is offline. Unlike the offline leveling, it doesn't fight.
/// </summary>
public sealed class OfflineShopPlayer : OfflinePlayer
{
    private readonly bool _staysOnLogin;

    /// <summary>
    /// Initializes a new instance of the <see cref="OfflineShopPlayer"/> class.
    /// </summary>
    /// <param name="gameContext">The game context.</param>
    /// <param name="expiresAtUtc">When the store closes; <c>null</c> for never.</param>
    /// <param name="staysOnLogin">If the store stays open when the account logs in with another character.</param>
    public OfflineShopPlayer(IGameContext gameContext, DateTime? expiresAtUtc, bool staysOnLogin)
        : base(gameContext)
    {
        this.ExpiresAtUtc = expiresAtUtc;
        this._staysOnLogin = staysOnLogin;
    }

    /// <summary>
    /// Gets when the store closes; <c>null</c> for never.
    /// </summary>
    public DateTime? ExpiresAtUtc { get; }

    /// <inheritdoc />
    public override bool StaysOnLogin => this._staysOnLogin;

    /// <inheritdoc />
    protected override void StartIntelligence()
    {
        // A store just waits for its buyers.
    }
}
