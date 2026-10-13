namespace MuCustom.PlugIns.Common;

using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MuCustom.PlugIns.Data;
using MuCustom.PlugIns.OffStore;
using MuCustom.PlugIns.Quests;
using MuCustom.PlugIns.Vip;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Offline;
using MUnique.OpenMU.GameLogic.PlugIns;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Loads and unloads the MuCustom data of a character when it enters and leaves the world,
/// and protects the character of an offline store which stays open (Diamante).
/// </summary>
[PlugIn]
[Display(Name = "MuCustom: ciclo del jugador", Description = "Carga VIP, fichas y misiones diarias al entrar al juego y las guarda al salir.")]
[Guid("8F4C2A6D-1E3B-4D97-A0C5-7B2E9F1D3C48")]
public class PlayerLifecyclePlugIn : IPlayerStateChangedPlugIn, ICharacterSelectingPlugIn, IPlayerLeavingWorldPlugIn, IPlayerLeftWorldPlugIn
{
    /// <inheritdoc />
    public async ValueTask PlayerLeftWorldAsync(Player player)
    {
        // The character is saved and the account is still logged in, so nobody can enter with it
        // before the offline store exists. A disconnecting player is already in the disconnected state.
        try
        {
            await OffStoreService.StartPendingAsync(player, player.PlayerState.CurrentState == PlayerState.Disconnected).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            player.Logger.LogError(ex, "MuCustom: could not start the offline store of {Player}.", player.Name);
        }
    }

    /// <inheritdoc />
    public async ValueTask PlayerLeavingWorldAsync(Player player)
    {
        // A player who leaves with an open personal store keeps it open offline, if the membership allows it.
        try
        {
            await OffStoreService.CaptureLeavingAsync(player).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            player.Logger.LogError(ex, "MuCustom: could not check the offline store of {Player}.", player.Name);
        }
    }

    /// <inheritdoc />
    public async ValueTask PlayerStateChangedAsync(Player player, State previousState, State currentState)
    {
        if (player is OfflinePlayer)
        {
            return;
        }

        try
        {
            if (currentState == PlayerState.CharacterSelection)
            {
                // The account is assigned after the login state change, so this is the first state which has it.
                OffStoreService.OnCharacterSelection(player);
            }
            else if (currentState == PlayerState.EnteredWorld && previousState == PlayerState.CharacterSelection)
            {
                await this.OnEnteredWorldAsync(player).ConfigureAwait(false);
            }
            else if (IsOutOfTheWorld(currentState) && !IsOutOfTheWorld(previousState))
            {
                VipService.Forget(player);
                await DailyQuestService.UnloadAsync(player, player.Logger).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            player.Logger.LogError(ex, "MuCustom: error on the state change of {Player} to {State}.", player.Name, currentState);
        }
    }

    /// <inheritdoc />
    public async ValueTask CharacterSelectingAsync(Player player, Character character, CancelEventArgs cancelEventArgs)
    {
        // Playing again in this session: a store which waited for the disconnection is not kept open.
        OffStoreService.DiscardPending(player);

        if (await OffStoreService.PreventSelectionAsync(player, character.Name).ConfigureAwait(false))
        {
            player.Logger.LogInformation("MuCustom: {Character} had an offline store during this session; the player has to log in again to use it.", character.Name);
            cancelEventArgs.Cancel = true;
        }
    }

    // The player state also leaves EnteredWorld for dialogs, trades, deaths and map changes, which are still in the world.
    private static bool IsOutOfTheWorld(State state)
        => state == PlayerState.Initial
           || state == PlayerState.LoginScreen
           || state == PlayerState.Authenticated
           || state == PlayerState.CharacterSelection
           || state == PlayerState.Disconnected
           || state == PlayerState.Finished;

    private async ValueTask OnEnteredWorldAsync(Player player)
    {
        if (!CustomDatabase.IsReady || player.Account is not { } account)
        {
            return;
        }

        await CashRepository.EnsureAccountAsync(account.GetId()).ConfigureAwait(false);
        await VipService.RefreshAsync(player).ConfigureAwait(false);
        await DailyQuestService.LoadAsync(player).ConfigureAwait(false);

        if (!MuCustomSettings.Current.ShowVipStatusOnEnter)
        {
            return;
        }

        var balance = await CashRepository.GetBalanceAsync(account.GetId()).ConfigureAwait(false);
        var (level, expiresAt) = VipService.GetEffective(player);
        var status = level.Level == 0
            ? "sin membresia"
            : $"{level.Name} hasta {expiresAt:dd/MM/yyyy HH:mm} UTC";
        await player.ShowBlueMessageAsync($"[VIP] {status} | Fichas: {balance:N0} | /vip para ver beneficios").ConfigureAwait(false);
    }
}
