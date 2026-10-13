namespace MuCustom.PlugIns.Vip;

using MuCustom.PlugIns.Data;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;

/// <summary>
/// Switches between the vaults of an account (<c>/baul</c>).
/// </summary>
/// <remarks>
/// Every vault is an OpenMU item storage. The one in use is the vault of the account, which the
/// vault NPC (and <c>/openware</c>) opens; switching sets another storage as the account's vault.
/// <c>account_vaults</c> remembers which storage is which number, so the active number is the one
/// whose storage is the account's vault. The new storage is recorded before the account is saved,
/// so a failure can't lose a vault: at worst an empty storage stays unused.
/// </remarks>
public static class VaultService
{
    /// <summary>
    /// The maximum number of vaults.
    /// </summary>
    public const int MaximumVaults = 5;

    /// <summary>
    /// Gets the number of the vault in use.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <returns>The vault number.</returns>
    public static async ValueTask<int> GetActiveVaultAsync(Player player)
    {
        if (player.Account is not { } account || account.Vault is not { } vault)
        {
            return 1;
        }

        var vaults = await VaultRepository.GetVaultsAsync(account.GetId()).ConfigureAwait(false);
        return vaults.FirstOrDefault(v => v.Value == vault.GetId()).Key is var number and > 0 ? number : 1;
    }

    /// <summary>
    /// Switches to another vault.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="target">The number of the vault.</param>
    /// <returns>The message for the player.</returns>
    public static async ValueTask<string> SwitchAsync(Player player, int target)
    {
        if (player.Account is not { } account)
        {
            return "No hay una cuenta cargada.";
        }

        if (player.PlayerState.CurrentState != PlayerState.EnteredWorld)
        {
            return "Cierra el baul, el comercio o la ventana del NPC antes de cambiar de baul.";
        }

        var allowed = VipService.GetLevel(player).Vaults;
        if (target < 1 || target > MaximumVaults)
        {
            return $"El baul debe ser un numero de 1 a {MaximumVaults}.";
        }

        if (target > allowed)
        {
            return $"Tu membresia permite {allowed} baul(es). Mejora tu VIP para usar el baul {target} (/vip).";
        }

        var vaults = await VaultRepository.GetVaultsAsync(account.GetId()).ConfigureAwait(false);
        var switched = await player.RunPersistenceExclusiveAsync(async () =>
        {
            var current = account.Vault ??= player.PersistenceContext.CreateNew<ItemStorage>();
            var currentNumber = vaults.FirstOrDefault(v => v.Value == current.GetId()).Key;
            if (currentNumber == 0)
            {
                // First use (it's vault 1), or the account got another vault in some other way: then it takes a free number.
                currentNumber = Enumerable.Range(1, MaximumVaults).FirstOrDefault(n => !vaults.ContainsKey(n));
                if (currentNumber == 0)
                {
                    return (Done: false, Message: "No se pudo identificar el baul actual. Avisa a un administrador.");
                }

                await VaultRepository.SetVaultAsync(account.GetId(), currentNumber, current.GetId()).ConfigureAwait(false);
                vaults[currentNumber] = current.GetId();
            }

            if (currentNumber == target)
            {
                return (Done: false, Message: $"Ya estas usando el baul {target}.");
            }

            ItemStorage? next = null;
            if (vaults.TryGetValue(target, out var storageId))
            {
                next = await player.PersistenceContext.GetByIdAsync<ItemStorage>(storageId).ConfigureAwait(false);
            }

            if (next is null)
            {
                next = player.PersistenceContext.CreateNew<ItemStorage>();
                await VaultRepository.SetVaultAsync(account.GetId(), target, next.GetId()).ConfigureAwait(false);
            }

            account.Vault = next;
            player.Vault = null;
            return (Done: true, Message: string.Empty);
        }).ConfigureAwait(false);

        if (!switched.Done)
        {
            return switched.Message;
        }

        if (!await player.SaveProgressAsync().ConfigureAwait(false))
        {
            return "No se pudo guardar el cambio de baul; intentalo de nuevo.";
        }

        await VaultRepository.SetActiveVaultAsync(account.GetId(), target).ConfigureAwait(false);
        return $"Ahora usas el baul {target} de {allowed}. Abrelo en el NPC del baul.";
    }
}
