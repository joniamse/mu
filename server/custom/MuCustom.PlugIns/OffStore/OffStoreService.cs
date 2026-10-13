namespace MuCustom.PlugIns.OffStore;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using MuCustom.PlugIns.Data;
using MuCustom.PlugIns.Vip;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Offline;
using MUnique.OpenMU.GameLogic.PlayerActions.PlayerStore;

/// <summary>
/// Offline stores: a personal store which stays in the world while its owner is offline.
/// </summary>
/// <remarks>
/// <para>
/// The store character becomes an <see cref="OfflineShopPlayer"/>, which OpenMU manages like an
/// offline leveling session. It starts by itself when a VIP player leaves the game with the store
/// open in a town (or with <c>/offstore</c>). Logging in again on the same game server stops it
/// and loads the account with its sales. With Diamante it stays open while the account plays
/// another character.
/// </para>
/// <para>
/// Safety rules, so a character never exists twice and nothing is duplicated:
/// <list type="bullet">
/// <item>An offline store loads the character from the database after the player's data was saved.</item>
/// <item>A character with an open store can't be played: selecting it closes the store and disconnects,
/// so the next login loads its current data. The same applies to characters whose store was open when
/// the account was loaded (their data in the session is old).</item>
/// <item>Logging in on another game server of this process closes the store there (except Diamante).</item>
/// <item>Every sale saves seller and buyer at once; sold items are written by OpenMU when sold.</item>
/// <item>Open stores are recorded in <c>offline_stores</c> and restored after a restart or crash,
/// from the database state, with their absolute expiration (the downtime counts).</item>
/// </list>
/// </para>
/// </remarks>
public static class OffStoreService
{
    private static readonly OpenStoreAction OpenStoreAction = new();
    private static readonly ConditionalWeakTable<Player, HashSet<string>> LockedCharacters = new();
    private static readonly ConditionalWeakTable<Player, PendingStore> PendingStores = new();
    private static readonly ConcurrentDictionary<int, byte> RestoredServers = new();

    /// <summary>
    /// Turns the player's open store into an offline store (<c>/offstore</c>).
    /// </summary>
    /// <param name="player">The player.</param>
    /// <returns>An error message, or <c>null</c> when the store stays open.</returns>
    public static async ValueTask<string?> StartAsync(Player player)
    {
        var level = VipService.GetLevel(player);
        if (!level.HasOffStore)
        {
            return "La tienda offline requiere VIP Plata o superior (/vip).";
        }

        if (player.SelectedCharacter is not { } character || player.Account?.LoginName is not { } loginName)
        {
            return "No hay un personaje seleccionado.";
        }

        if (player.ShopStorage is not { StoreOpen: true } shop || string.IsNullOrWhiteSpace(character.StoreName))
        {
            return "Primero abre tu tienda personal (I y luego S), ponle precio a los items y ábrela.";
        }

        if (!shop.Items.Any())
        {
            return "Tu tienda esta vacia.";
        }

        if (player.CurrentMiniGame is not null || player.CurrentMap is null || !player.IsAlive)
        {
            return "No puedes dejar una tienda offline aqui.";
        }

        // The store stays where the character stands: only in a town, where nobody can attack it.
        if (!player.IsAtSafezone())
        {
            return "La tienda offline solo se puede dejar en una ciudad (zona segura).";
        }

        if (FindOfflinePlayer(loginName) is not null)
        {
            return "Tu cuenta ya tiene una sesion offline activa (tienda o /offlevel).";
        }

        var expiresAt = level.OffStore is { } duration ? DateTime.UtcNow + duration : (DateTime?)null;
        var staysOnLogin = level.OffStoreWhilePlaying;
        var whilePlaying = staysOnLogin
            ? " Elige otro personaje para seguir jugando."
            : " Si vuelves a entrar con esta cuenta, la tienda se cierra.";
        await player.ShowBlueMessageAsync($"[Tienda] Tu tienda queda abierta {DescribeDuration(level.OffStore)}.{whilePlaying}").ConfigureAwait(false);

        var storeName = character.StoreName;
        var characterName = character.Name;
        var characterId = character.GetId();
        var accountId = player.Account.GetId();
        if (staysOnLogin)
        {
            // The player goes back to the character selection, with this character's data loaded
            // while the store starts selling: it stays locked for this session.
            LockCharacter(player, characterName);
        }

        // With Diamante the player goes back to the character selection to play another character;
        // otherwise the game closes, like the offline leveling does.
        var gameContext = player.GameContext;
        var manager = gameContext.OfflinePlayerManager;
        if (!await manager.StartAsync(player, loginName, context => new OfflineShopPlayer(context, expiresAt, staysOnLogin), false, staysOnLogin).ConfigureAwait(false))
        {
            return "No se pudo iniciar la tienda offline.";
        }

        var record = new OffStoreRecord(characterId, accountId, loginName, characterName, storeName, GameServers.GetId(gameContext), staysOnLogin, DateTime.UtcNow, expiresAt, 0, 0);
        await OpenAndRecordAsync(gameContext, record).ConfigureAwait(false);
        return null;
    }

    /// <summary>
    /// Closes the offline store of the player's account, e.g. from another character (<c>/offstore cerrar</c>).
    /// </summary>
    /// <param name="player">The player.</param>
    /// <returns>The message for the player.</returns>
    public static async ValueTask<string> StopAsync(Player player)
    {
        if (player.Account?.LoginName is not { } loginName
            || FindOfflinePlayer(loginName) is not (OfflineShopPlayer shop, var gameContext))
        {
            return "Tu cuenta no tiene una tienda offline abierta.";
        }

        var name = shop.Name;
        LockCharacter(player, name);
        shop.CloseReason = "la cerraste";
        await gameContext.OfflinePlayerManager.StopAsync(loginName).ConfigureAwait(false);
        return $"Se cerro la tienda offline de {name}. Para jugar con {name}, vuelve a conectarte.";
    }

    /// <summary>
    /// Closes the stores which expired or sold everything.
    /// </summary>
    /// <param name="gameContext">The game context.</param>
    /// <returns>The task.</returns>
    public static async ValueTask CloseExpiredAsync(IGameContext gameContext)
    {
        var now = DateTime.UtcNow;
        foreach (var shop in gameContext.OfflinePlayerManager.OfflinePlayers.OfType<OfflineShopPlayer>())
        {
            var expired = shop.ExpiresAtUtc <= now;
            var soldOut = shop.ShopStorage is { } storage && !storage.Items.Any();
            if ((expired || soldOut) && shop.AccountLoginName is { } loginName)
            {
                shop.CloseReason = expired ? "se termino el tiempo" : "vendiste todo";
                shop.Logger.LogInformation("MuCustom: closing the offline store of {Name} ({Reason}).", shop.Name, shop.CloseReason);
                await gameContext.OfflinePlayerManager.StopAsync(loginName).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// When a player comes to the character selection: closes the stores of the account on other game servers
    /// (except Diamante) and locks the characters of open stores for this session.
    /// </summary>
    /// <param name="player">The player at the character selection.</param>
    /// <returns>The task.</returns>
    public static async ValueTask OnCharacterSelectionAsync(Player player)
    {
        if (player.Account?.LoginName is not { } loginName)
        {
            return;
        }

        foreach (var gameContext in GameServers.All)
        {
            if (!gameContext.OfflinePlayerManager.TryGetPlayer(loginName, out var offlinePlayer) || offlinePlayer is null)
            {
                continue;
            }

            // Its data in this session was loaded while it was offline, so it can't be played in this session.
            if (offlinePlayer.Name is { Length: > 0 } name)
            {
                LockCharacter(player, name);
            }

            // On the same game server OpenMU already stopped the other sessions at the login.
            if (gameContext != player.GameContext && !offlinePlayer.StaysOnLogin)
            {
                if (offlinePlayer is OfflineShopPlayer shop)
                {
                    shop.CloseReason = "entraste con la cuenta";
                }

                await gameContext.OfflinePlayerManager.StopAsync(loginName).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Determines whether the character can't be selected by the player in this session,
    /// and closes its store if it is still open.
    /// </summary>
    /// <param name="player">The player at the character selection.</param>
    /// <param name="characterName">The name of the character.</param>
    /// <returns><c>true</c> if the selection must be prevented.</returns>
    public static async ValueTask<bool> PreventSelectionAsync(Player player, string characterName)
    {
        var locked = false;
        if (LockedCharacters.TryGetValue(player, out var names))
        {
            lock (names)
            {
                locked = names.Contains(characterName);
            }
        }

        var storeIsOpen = false;
        if (player.Account?.LoginName is { } loginName
            && FindOfflinePlayer(loginName) is ({ } offlinePlayer, var gameContext)
            && offlinePlayer.Name == characterName)
        {
            storeIsOpen = true;
            if (offlinePlayer is OfflineShopPlayer shop)
            {
                shop.CloseReason = "elegiste el personaje";
            }

            await gameContext.OfflinePlayerManager.StopAsync(loginName).ConfigureAwait(false);
        }

        return locked || storeIsOpen;
    }

    /// <summary>
    /// Remembers, while the player leaves the world with an open personal store, what's needed to
    /// keep the store open offline. The store starts when the player's data has been saved:
    /// see <see cref="StartPendingAsync"/>.
    /// </summary>
    /// <param name="player">The player who leaves the world.</param>
    /// <returns>The task.</returns>
    public static async ValueTask CaptureLeavingAsync(Player player)
    {
        PendingStores.Remove(player);
        if (player is OfflinePlayer
            || player.SelectedCharacter is not { } character
            || player.Account is not { LoginName: { } loginName } account
            || player.ShopStorage is not { StoreOpen: true } shop
            || !shop.Items.Any()
            || string.IsNullOrWhiteSpace(character.StoreName)
            || player.CurrentMiniGame is not null
            || !player.IsAtSafezone()
            || !CustomDatabase.IsReady
            || FindOfflinePlayer(loginName) is not null)
        {
            return;
        }

        // Read from the database: when disconnecting, the cached membership is already gone.
        var status = await VipRepository.GetStatusAsync(account.GetId(), character.GetId()).ConfigureAwait(false);
        var level = status.GetEffective(DateTime.UtcNow).Level;
        if (!level.HasOffStore)
        {
            return;
        }

        var expiresAt = level.OffStore is { } duration ? DateTime.UtcNow + duration : (DateTime?)null;
        var record = new OffStoreRecord(character.GetId(), account.GetId(), loginName, character.Name, character.StoreName, GameServers.GetId(player.GameContext), level.OffStoreWhilePlaying, DateTime.UtcNow, expiresAt, 0, 0);
        PendingStores.AddOrUpdate(player, new PendingStore(record));
        if (level.OffStoreWhilePlaying)
        {
            LockCharacter(player, character.Name);
        }
    }

    /// <summary>
    /// Opens the offline store which was captured when the player left the world.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="disconnected"><c>true</c> when the connection is closing; <c>false</c> when the player left the world but is still connected.</param>
    /// <returns>The task.</returns>
    public static async ValueTask StartPendingAsync(Player player, bool disconnected)
    {
        if (!PendingStores.TryGetValue(player, out var pending))
        {
            return;
        }

        // Still connected (back to the character or server selection): the account stays logged in, and only
        // Diamante allows playing meanwhile. For the others the store waits for the disconnection - "select
        // server" disconnects right after - and is discarded if the player enters the world again.
        var record = pending.Record;
        if (!disconnected && !record.StaysOnLogin)
        {
            return;
        }

        PendingStores.Remove(player);
        var gameContext = player.GameContext;
        if (!await gameContext.OfflinePlayerManager.StartAsync(gameContext, record.LoginName, record.CharacterName, context => new OfflineShopPlayer(context, record.ExpiresAt, record.StaysOnLogin)).ConfigureAwait(false))
        {
            player.Logger.LogWarning("MuCustom: the offline store of {Character} could not be started.", record.CharacterName);
            return;
        }

        await OpenAndRecordAsync(gameContext, record).ConfigureAwait(false);
        player.Logger.LogInformation("MuCustom: the store of {Character} stays open offline.", record.CharacterName);
    }

    /// <summary>
    /// Forgets a store which waited for the disconnection, because the player selects a character again.
    /// </summary>
    /// <param name="player">The player.</param>
    public static void DiscardPending(Player player) => PendingStores.Remove(player);

    /// <summary>
    /// Restores the offline stores of a game server after a restart or a crash. Runs once per game server.
    /// </summary>
    /// <param name="gameContext">The game context.</param>
    /// <param name="logger">The logger.</param>
    /// <returns>The task.</returns>
    public static async ValueTask RestoreAsync(IGameContext gameContext, ILogger logger)
    {
        var serverId = GameServers.GetId(gameContext);
        if (serverId < 0 || !RestoredServers.TryAdd(serverId, 0))
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var record in await OffStoreRepository.GetByServerAsync(serverId).ConfigureAwait(false))
        {
            try
            {
                var closeReason = await GetRestoreObstacleAsync(record, now).ConfigureAwait(false);
                if (closeReason is null
                    && !await gameContext.OfflinePlayerManager.StartAsync(gameContext, record.LoginName, record.CharacterName, context => new OfflineShopPlayer(context, record.ExpiresAt, record.StaysOnLogin)).ConfigureAwait(false))
                {
                    closeReason = "no se pudo reabrir tras el reinicio";
                }

                if (closeReason is not null)
                {
                    await OffStoreRepository.CloseAsync(record.CharacterId, closeReason).ConfigureAwait(false);
                    logger.LogInformation("MuCustom: the offline store of {Character} was not restored ({Reason}).", record.CharacterName, closeReason);
                    continue;
                }

                await OpenAndRecordAsync(gameContext, record).ConfigureAwait(false);
                logger.LogInformation("MuCustom: restored the offline store of {Character}.", record.CharacterName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "MuCustom: error restoring the offline store of {Character}.", record.CharacterName);
            }
        }
    }

    /// <summary>
    /// Counts a sale of an offline store and saves seller and buyer at once, so a crash right after
    /// the sale can't leave the buyer with both the item and the zen.
    /// </summary>
    /// <param name="seller">The seller.</param>
    /// <param name="buyer">The buyer.</param>
    /// <returns>The task.</returns>
    public static async ValueTask OnItemSoldAsync(Player seller, Player buyer)
    {
        await seller.SaveProgressAsync().ConfigureAwait(false);
        await buyer.SaveProgressAsync().ConfigureAwait(false);
        if (seller is OfflineShopPlayer shop && shop.SelectedCharacter is { } character && CustomDatabase.IsReady)
        {
            var earned = shop.TakeEarnedMoney();
            await OffStoreRepository.AddSaleAsync(character.GetId(), earned).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Shows the player the summary of the stores which closed while it was away, and the stores of
    /// the account which are still open with their remaining time.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <returns>The task.</returns>
    public static async ValueTask ShowStoreStatusAsync(Player player)
    {
        if (player.Account is not { } account || !CustomDatabase.IsReady)
        {
            return;
        }

        foreach (var report in await OffStoreRepository.TakeUnnotifiedAsync(account.GetId()).ConfigureAwait(false))
        {
            var hours = report.ClosedAt - report.StartedAt;
            await player.ShowBlueMessageAsync(
                $"[Tienda] La tienda offline de {report.CharacterName} se cerro ({report.Reason}) tras {(int)hours.TotalHours}h {hours.Minutes:00}m. Vendiste {report.SoldItems} item(s) por {report.SoldZen:N0} zen.")
                .ConfigureAwait(false);
        }

        foreach (var line in await DescribeOpenStoresAsync(account.GetId()).ConfigureAwait(false))
        {
            await player.ShowBlueMessageAsync(line).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Describes the open offline stores of an account, e.g. <c>[Tienda] Kris VIP de test0Dk: quedan 10h 12m...</c>.
    /// </summary>
    /// <param name="accountId">The account identifier.</param>
    /// <returns>One line per store.</returns>
    public static async ValueTask<IReadOnlyList<string>> DescribeOpenStoresAsync(Guid accountId)
    {
        var stores = await OffStoreRepository.GetByAccountAsync(accountId).ConfigureAwait(false);
        return stores
            .Select(s => $"[Tienda] \"{s.StoreName}\" de {s.CharacterName} sigue abierta: {(s.ExpiresAt is null ? "sin limite de tiempo" : "quedan " + DescribeRemaining(s.ExpiresAt))}. Vendidos: {s.SoldItems} item(s) por {s.SoldZen:N0} zen.")
            .ToList();
    }

    /// <summary>
    /// Describes the store of a player who has no open offline store: the shop it has open right now
    /// (what happens to it when it leaves) and the last offline store of the account (when and why it
    /// closed).
    /// </summary>
    /// <param name="player">The player.</param>
    /// <returns>Short lines, without the <c>[Tienda]</c> prefix.</returns>
    public static async ValueTask<IReadOnlyList<string>> DescribeStoreHelpAsync(Player player)
    {
        var lines = new List<string>();
        var level = VipService.GetLevel(player);
        if (player.ShopStorage is { StoreOpen: true })
        {
            lines.Add(level.HasOffStore
                ? $"Tu tienda esta abierta en linea. Si sales del juego con ella abierta, queda offline {DescribeDuration(level.OffStore)}."
                : "Tu tienda esta abierta en linea; se cierra al salir (la tienda offline es un beneficio VIP, /vip).");
            if (level.HasOffStore && !level.OffStoreWhilePlaying)
            {
                lines.Add("La tienda offline se cierra cuando vuelves a entrar con la cuenta (con Diamante sigue abierta mientras juegas).");
            }
        }

        if (player.Account is { } account && CustomDatabase.IsReady
            && await OffStoreRepository.GetLastClosedAsync(account.GetId()).ConfigureAwait(false) is { } last)
        {
            var open = last.ClosedAt - last.StartedAt;
            lines.Add($"Tu ultima tienda offline ({last.CharacterName}) se cerro hace {DescribeAgo(last.ClosedAt)}: {last.Reason}, tras {(int)open.TotalHours}h {open.Minutes:00}m. Vendiste {last.SoldItems} item(s) por {last.SoldZen:N0} zen.");
        }

        return lines;
    }

    /// <summary>
    /// Describes how long ago something happened, e.g. <c>3h 05m</c>.
    /// </summary>
    /// <param name="when">When, in UTC.</param>
    /// <returns>The text.</returns>
    public static string DescribeAgo(DateTime when)
    {
        var ago = DateTime.UtcNow - DateTime.SpecifyKind(when, DateTimeKind.Utc);
        if (ago.TotalDays >= 1)
        {
            return $"{(int)ago.TotalDays}d {ago.Hours}h";
        }

        return ago.TotalHours >= 1 ? $"{(int)ago.TotalHours}h {ago.Minutes:00}m" : $"{Math.Max(1, (int)ago.TotalMinutes)}m";
    }

    /// <summary>
    /// Describes how long a store stays open.
    /// </summary>
    /// <param name="duration">The duration; <c>null</c> for unlimited.</param>
    /// <returns>The text.</returns>
    public static string DescribeDuration(TimeSpan? duration)
        => duration is { } d ? $"durante {d.TotalHours:0} horas" : "sin limite de tiempo";

    /// <summary>
    /// Describes the remaining time of a store.
    /// </summary>
    /// <param name="expiresAt">When it closes; <c>null</c> for never.</param>
    /// <returns>The text, e.g. <c>10h 12m</c>.</returns>
    public static string DescribeRemaining(DateTime? expiresAt)
    {
        if (expiresAt is not { } expires)
        {
            return "sin limite";
        }

        var left = expires - DateTime.UtcNow;
        if (left <= TimeSpan.Zero)
        {
            return "cerrandose";
        }

        return left.TotalHours >= 1 ? $"{(int)left.TotalHours}h {left.Minutes:00}m" : $"{Math.Max(1, left.Minutes)}m";
    }

    // Opens the store of a started offline player and records it, or closes it at once if there is nothing to sell.
    private static async ValueTask OpenAndRecordAsync(IGameContext gameContext, OffStoreRecord record)
    {
        if (!gameContext.OfflinePlayerManager.TryGetPlayer(record.LoginName, out var offlinePlayer) || offlinePlayer is not OfflineShopPlayer shop)
        {
            return;
        }

        if (CustomDatabase.IsReady)
        {
            await OffStoreRepository.SaveAsync(record with { ServerId = GameServers.GetId(gameContext) }).ConfigureAwait(false);
        }

        shop.StartCountingMoney();
        if (shop.ShopStorage is not { } storage || !storage.Items.Any())
        {
            shop.CloseReason = "vendiste todo";
            await gameContext.OfflinePlayerManager.StopAsync(record.LoginName).ConfigureAwait(false);
            return;
        }

        if (!storage.StoreOpen)
        {
            await OpenStoreAction.OpenStoreAsync(shop, record.StoreName).ConfigureAwait(false);
        }
    }

    // Why a recorded store can't be restored, or null if it can.
    private static async ValueTask<string?> GetRestoreObstacleAsync(OffStoreRecord record, DateTime now)
    {
        if (record.ExpiresAt <= now)
        {
            return "se termino el tiempo";
        }

        var status = await VipRepository.GetStatusAsync(record.AccountId, record.CharacterId).ConfigureAwait(false);
        if (!status.GetEffective(now).Level.HasOffStore)
        {
            return "vencio la membresia VIP";
        }

        // The account is playing: restoring would put the character in the world twice.
        if (FindOfflinePlayer(record.LoginName) is not null || await IsAccountOnlineAsync(record.LoginName).ConfigureAwait(false))
        {
            return "entraste con la cuenta";
        }

        return null;
    }

    private static async ValueTask<bool> IsAccountOnlineAsync(string loginName)
    {
        foreach (var gameContext in GameServers.All)
        {
            var players = await gameContext.GetPlayersAsync().ConfigureAwait(false);
            if (players.Any(p => p is not OfflinePlayer && string.Equals(p.Account?.LoginName, loginName, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    // The offline session of an account on any game server of this process.
    private static (OfflinePlayer Player, IGameContext GameContext)? FindOfflinePlayer(string loginName)
    {
        foreach (var gameContext in GameServers.All)
        {
            if (gameContext.OfflinePlayerManager.TryGetPlayer(loginName, out var offlinePlayer) && offlinePlayer is not null)
            {
                return (offlinePlayer, gameContext);
            }
        }

        return null;
    }

    private static void LockCharacter(Player player, string characterName)
    {
        var names = LockedCharacters.GetValue(player, _ => new HashSet<string>(StringComparer.Ordinal));
        lock (names)
        {
            names.Add(characterName);
        }
    }
}

/// <summary>
/// An open personal store whose character left the world, waiting to stay open offline.
/// </summary>
/// <param name="Record">The store.</param>
internal sealed record PendingStore(OffStoreRecord Record);
