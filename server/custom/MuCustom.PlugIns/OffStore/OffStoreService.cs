namespace MuCustom.PlugIns.OffStore;

using System.Runtime.CompilerServices;
using MuCustom.PlugIns.Data;
using MuCustom.PlugIns.Vip;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Offline;
using MUnique.OpenMU.GameLogic.PlayerActions.PlayerStore;

/// <summary>
/// Offline stores (<c>/offstore</c>), on top of the offline sessions of OpenMU.
/// </summary>
/// <remarks>
/// The store character becomes an <see cref="OfflineShopPlayer"/>, which OpenMU manages like an
/// offline leveling session: logging in again stops it and loads the account with its sales.
/// With Diamante the store stays open while the account plays another character. That character's
/// data was loaded while the store was still selling, so it can't be selected in that session:
/// selecting it closes the store and disconnects, and the next login loads the current data.
/// </remarks>
public static class OffStoreService
{
    private static readonly OpenStoreAction OpenStoreAction = new();
    private static readonly ConditionalWeakTable<Player, HashSet<string>> LockedCharacters = new();
    private static readonly ConditionalWeakTable<Player, PendingStore> PendingStores = new();

    /// <summary>
    /// Turns the player's open store into an offline store and disconnects the player.
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
            return "Primero abre tu tienda personal (tecla S), ponle precio a los items y luego escribe /offstore.";
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

        var manager = player.GameContext.OfflinePlayerManager;
        if (manager.IsActive(loginName))
        {
            return "Tu cuenta ya tiene una sesion offline activa (tienda o /offlevel).";
        }

        var expiresAt = level.OffStore is { } duration ? DateTime.UtcNow + duration : (DateTime?)null;
        var staysOnLogin = level.OffStoreWhilePlaying;
        var until = expiresAt is { } e ? $"durante {level.OffStore!.Value.TotalHours:0} horas" : "sin limite de tiempo";
        var whilePlaying = staysOnLogin
            ? " Elige otro personaje para seguir jugando."
            : " Si vuelves a entrar con esta cuenta, la tienda se cierra.";
        await player.ShowBlueMessageAsync($"[Tienda] Tu tienda queda abierta {until}.{whilePlaying}").ConfigureAwait(false);

        var storeName = character.StoreName;
        if (staysOnLogin)
        {
            // The player goes back to the character selection, with this character's data loaded
            // while the store starts selling: it stays locked for this session.
            LockCharacter(player, character.Name);
        }

        // With Diamante the player goes back to the character selection to play another character;
        // otherwise the game closes, like the offline leveling does.
        if (!await manager.StartAsync(player, loginName, context => new OfflineShopPlayer(context, expiresAt, staysOnLogin), false, staysOnLogin).ConfigureAwait(false))
        {
            return "No se pudo iniciar la tienda offline.";
        }

        if (manager.TryGetPlayer(loginName, out var offlinePlayer) && offlinePlayer is { ShopStorage.StoreOpen: false })
        {
            await OpenStoreAction.OpenStoreAsync(offlinePlayer, storeName).ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>
    /// Closes the offline store of the player's account, e.g. from another character.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <returns>The message for the player.</returns>
    public static async ValueTask<string> StopAsync(Player player)
    {
        if (player.Account?.LoginName is not { } loginName
            || !player.GameContext.OfflinePlayerManager.TryGetPlayer(loginName, out var offlinePlayer)
            || offlinePlayer is not OfflineShopPlayer shop)
        {
            return "Tu cuenta no tiene una tienda offline abierta.";
        }

        var name = shop.Name;
        LockCharacter(player, name);
        await player.GameContext.OfflinePlayerManager.StopAsync(loginName).ConfigureAwait(false);
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
            var expired = shop.ExpiresAtUtc < now;
            var soldOut = shop.ShopStorage is { } storage && !storage.Items.Any();
            if ((expired || soldOut) && shop.AccountLoginName is { } loginName)
            {
                shop.Logger.LogInformation("MuCustom: closing the offline store of {Name} ({Reason}).", shop.Name, expired ? "expired" : "sold out");
                await gameContext.OfflinePlayerManager.StopAsync(loginName).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Remembers, when a player comes to the character selection, the character of an offline store which stays open.
    /// Its data was loaded with the account while the store was selling, so it stays locked for the session.
    /// </summary>
    /// <param name="player">The player at the character selection.</param>
    public static void OnCharacterSelection(Player player)
    {
        if (player.Account?.LoginName is { } loginName
            && player.GameContext.OfflinePlayerManager.TryGetPlayer(loginName, out var offlinePlayer)
            && offlinePlayer is OfflineShopPlayer { StaysOnLogin: true } shop
            && shop.Name is { Length: > 0 } name)
        {
            LockCharacter(player, name);
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

        var loginName = player.Account?.LoginName;
        var storeIsOpen = loginName is not null
            && player.GameContext.OfflinePlayerManager.TryGetPlayer(loginName, out var offlinePlayer)
            && offlinePlayer is OfflineShopPlayer shop
            && shop.Name == characterName;
        if (storeIsOpen)
        {
            await player.GameContext.OfflinePlayerManager.StopAsync(loginName!).ConfigureAwait(false);
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
            || player.Account?.LoginName is not { } loginName
            || player.ShopStorage is not { StoreOpen: true } shop
            || !shop.Items.Any()
            || string.IsNullOrWhiteSpace(character.StoreName)
            || player.CurrentMiniGame is not null
            || !player.IsAtSafezone()
            || player.GameContext.OfflinePlayerManager.IsActive(loginName))
        {
            return;
        }

        // Read from the database: when disconnecting, the cached membership is already gone.
        if (!CustomDatabase.IsReady || player.Account is not { } account)
        {
            return;
        }

        var status = await VipRepository.GetStatusAsync(account.GetId(), character.GetId()).ConfigureAwait(false);
        var level = status.GetEffective(DateTime.UtcNow).Level;
        if (!level.HasOffStore)
        {
            return;
        }

        var expiresAt = level.OffStore is { } duration ? DateTime.UtcNow + duration : (DateTime?)null;
        PendingStores.AddOrUpdate(player, new PendingStore(loginName, character.Name, character.StoreName, expiresAt, level.OffStoreWhilePlaying));
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
        if (!disconnected && !pending.StaysOnLogin)
        {
            return;
        }

        PendingStores.Remove(player);

        var manager = player.GameContext.OfflinePlayerManager;
        if (!await manager.StartAsync(player.GameContext, pending.LoginName, pending.CharacterName, context => new OfflineShopPlayer(context, pending.ExpiresAtUtc, pending.StaysOnLogin)).ConfigureAwait(false))
        {
            player.Logger.LogWarning("MuCustom: the offline store of {Character} could not be started.", pending.CharacterName);
            return;
        }

        if (manager.TryGetPlayer(pending.LoginName, out var offlinePlayer) && offlinePlayer is { ShopStorage.StoreOpen: false })
        {
            await OpenStoreAction.OpenStoreAsync(offlinePlayer, pending.StoreName).ConfigureAwait(false);
        }

        player.Logger.LogInformation("MuCustom: the store of {Character} stays open offline.", pending.CharacterName);
    }

    /// <summary>
    /// Forgets a store which waited for the disconnection, because the player selects a character again.
    /// </summary>
    /// <param name="player">The player.</param>
    public static void DiscardPending(Player player) => PendingStores.Remove(player);

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
/// <param name="LoginName">The account login name.</param>
/// <param name="CharacterName">The character name.</param>
/// <param name="StoreName">The name of the store.</param>
/// <param name="ExpiresAtUtc">When the store closes; <c>null</c> for never.</param>
/// <param name="StaysOnLogin">If the store stays open while the account plays another character.</param>
internal sealed record PendingStore(string LoginName, string CharacterName, string StoreName, DateTime? ExpiresAtUtc, bool StaysOnLogin);
