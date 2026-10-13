namespace MuCustom.PlugIns.Common;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MuCustom.PlugIns.Announcements;
using MuCustom.PlugIns.Data;
using MuCustom.PlugIns.OffStore;
using MuCustom.PlugIns.Quests;
using MuCustom.PlugIns.Vip;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Offline;
using MUnique.OpenMU.GameLogic.PlugIns;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// The heart of the MuCustom features: holds their settings and runs the periodic work
/// (database migrations at start, saving the daily quest progress, refreshing the memberships
/// of the online players and closing expired offline stores).
/// </summary>
[PlugIn]
[Display(Name = "MuCustom: nucleo", Description = "Configuracion y tareas periodicas de VIP, fichas, misiones diarias, anuncios y tienda offline.")]
[Guid("3B6E1F0A-7C2D-4A58-9E41-6D0F2B8C5A71")]
public class MuCustomCorePlugIn : IPeriodicTaskPlugIn, ISupportCustomConfiguration<MuCustomSettings>, ISupportDefaultCustomConfiguration
{
    private static readonly TimeSpan QuestFlushInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan VipRefreshInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan OffStoreCheckInterval = TimeSpan.FromSeconds(15);

    private DateTime _nextQuestFlushUtc = DateTime.MinValue;
    private DateTime _nextVipRefreshUtc = DateTime.MinValue;
    private DateTime _nextOffStoreCheckUtc = DateTime.MinValue;
    private MuCustomSettings? _configuration;

    /// <inheritdoc />
    public MuCustomSettings? Configuration
    {
        get => this._configuration;
        set
        {
            this._configuration = value;
            MuCustomSettings.Current = value ?? new MuCustomSettings();
        }
    }

    /// <inheritdoc />
    public object CreateDefaultConfig() => new MuCustomSettings();

    /// <inheritdoc />
    public async ValueTask ExecuteTaskAsync(GameContext gameContext)
    {
        GameServers.Register(gameContext);
        var logger = gameContext.LoggerFactory.CreateLogger<MuCustomCorePlugIn>();
        if (!await CustomDatabase.EnsureMigratedAsync(logger).ConfigureAwait(false))
        {
            return;
        }

        // After a restart or a crash: the offline stores of this game server open again.
        await OffStoreService.RestoreAsync(gameContext, logger).ConfigureAwait(false);

        var now = DateTime.UtcNow;
        if (now >= this._nextQuestFlushUtc)
        {
            this._nextQuestFlushUtc = now + QuestFlushInterval;
            await DailyQuestService.FlushAsync(logger).ConfigureAwait(false);
            await DailyQuestService.ReloadQuestsAsync().ConfigureAwait(false);
        }

        if (now >= this._nextVipRefreshUtc)
        {
            this._nextVipRefreshUtc = now + VipRefreshInterval;
            await this.RefreshOnlinePlayersAsync(gameContext, logger).ConfigureAwait(false);
        }

        if (now >= this._nextOffStoreCheckUtc)
        {
            this._nextOffStoreCheckUtc = now + OffStoreCheckInterval;
            await OffStoreService.CloseExpiredAsync(gameContext).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void ForceStart()
    {
        this._nextQuestFlushUtc = DateTime.MinValue;
        this._nextVipRefreshUtc = DateTime.MinValue;
        this._nextOffStoreCheckUtc = DateTime.MinValue;
    }

    private async ValueTask RefreshOnlinePlayersAsync(GameContext gameContext, ILogger logger)
    {
        foreach (var player in await gameContext.GetPlayersAsync().ConfigureAwait(false))
        {
            if (player is OfflinePlayer || player.PlayerState.CurrentState != PlayerState.EnteredWorld)
            {
                continue;
            }

            try
            {
                var before = VipService.GetLevel(player);
                await VipService.RefreshAsync(player).ConfigureAwait(false);
                var after = VipService.GetLevel(player);
                if (before.Level > 0 && after.Level == 0)
                {
                    await player.ShowBlueMessageAsync($"[VIP] Tu membresia {before.Name} vencio. Renuevala con /vip.").ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "MuCustom: could not refresh the membership of {Player}.", player.Name);
            }
        }
    }
}
