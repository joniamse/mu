namespace MuCustom.PlugIns.OffStore;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MuCustom.PlugIns.Vip;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// <c>/tienda</c>: shows the open offline stores of the account with their remaining time and sales,
/// and how the offline store works with the player's membership.
/// </summary>
[PlugIn]
[Display(Name = "MuCustom: /tienda", Description = "Muestra las tiendas offline de la cuenta, el tiempo que les queda y lo vendido.")]
[Guid("7F3A9C24-1B6E-4D85-A0C9-5E2F8B1D4A37")]
[ChatCommandHelp(Command, CharacterStatus.Normal)]
public class StoreStatusChatCommandPlugIn : IChatCommandPlugIn
{
    private const string Command = "/tienda";

    /// <inheritdoc />
    public string Key => Command;

    /// <inheritdoc />
    public CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <inheritdoc />
    public ValueTask HandleCommandAsync(Player player, string command)
    {
        return ChatCommands.RunAsync(player, Command, async () =>
        {
            var lines = await OffStoreService.DescribeOpenStoresAsync(player.Account!.GetId()).ConfigureAwait(false);
            foreach (var line in lines)
            {
                await player.ShowBlueMessageAsync(line).ConfigureAwait(false);
            }

            var level = VipService.GetLevel(player);
            var help = level.HasOffStore
                ? $"Con tu VIP {level.Name}, si sales del juego con la tienda abierta en una ciudad, queda abierta offline {OffStoreService.DescribeDuration(level.OffStore)}."
                : "La tienda offline es un beneficio VIP (/vip): con Plata 12 h, con Oro 24 h, con Platino y Diamante sin limite.";
            if (lines.Count == 0)
            {
                await player.ShowBlueMessageAsync($"[Tienda] No tienes tiendas offline abiertas. {help}").ConfigureAwait(false);
            }
        });
    }
}
