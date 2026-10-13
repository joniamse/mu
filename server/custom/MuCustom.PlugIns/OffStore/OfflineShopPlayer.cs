namespace MuCustom.PlugIns.OffStore;

using MuCustom.PlugIns.Data;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Offline;

/// <summary>
/// The character of an offline store: it stays in the world with its personal store open while the
/// player is offline. Unlike the offline leveling, it doesn't fight.
/// </summary>
public sealed class OfflineShopPlayer : OfflinePlayer
{
    private readonly bool _staysOnLogin;
    private readonly object _moneyLock = new();
    private int _countedMoney;

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

    /// <summary>
    /// Gets or sets why the store closes, for the summary the owner sees at the next login.
    /// By default the owner logged in again.
    /// </summary>
    public string CloseReason { get; set; } = "volviste a entrar con la cuenta";

    /// <summary>
    /// Starts counting the earned zen from the current money.
    /// </summary>
    public void StartCountingMoney()
    {
        lock (this._moneyLock)
        {
            this._countedMoney = this.Money;
        }
    }

    /// <summary>
    /// Gets the zen earned since the last call.
    /// </summary>
    /// <returns>The earned zen.</returns>
    public long TakeEarnedMoney()
    {
        lock (this._moneyLock)
        {
            var earned = (long)this.Money - this._countedMoney;
            this._countedMoney = this.Money;
            return Math.Max(0, earned);
        }
    }

    /// <summary>
    /// Closes the store on purpose (not on a server shutdown, after which it's restored):
    /// it leaves the persistent records and goes to the history with its sales.
    /// </summary>
    /// <returns>The task.</returns>
    public override async ValueTask StopAsync()
    {
        try
        {
            if (this.SelectedCharacter is { } character && CustomDatabase.IsReady)
            {
                await OffStoreRepository.CloseAsync(character.GetId(), this.CloseReason).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            this.Logger.LogError(ex, "MuCustom: could not record the closing of the offline store of {Name}.", this.Name);
        }

        await base.StopAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override void StartIntelligence()
    {
        // A store just waits for its buyers.
    }
}
