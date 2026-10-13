namespace MuCustom.PlugIns.OffStore;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MuCustom.PlugIns.Common;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// <c>/offstore</c>: leaves the open personal store in the world while the player is offline;
/// <c>/offstore cerrar</c> closes it from another character (Diamante).
/// </summary>
[PlugIn]
[Display(Name = "MuCustom: /offstore", Description = "Tienda offline VIP: /offstore con la tienda abierta; /offstore cerrar.")]
[Guid("2C6F8B30-A9D4-4E71-B5C2-8E0D3A7F1B95")]
[ChatCommandHelp(Command, CharacterStatus.Normal)]
public class OffStoreChatCommandPlugIn : IChatCommandPlugIn
{
    private const string Command = "/offstore";

    /// <inheritdoc />
    public string Key => Command;

    /// <inheritdoc />
    public CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <inheritdoc />
    public ValueTask HandleCommandAsync(Player player, string command)
    {
        return ChatCommands.RunAsync(player, Command, async () =>
        {
            var action = ChatCommands.Arguments(command).FirstOrDefault()?.ToLowerInvariant();
            if (action is "cerrar" or "stop" or "close")
            {
                await player.ShowBlueMessageAsync($"[Tienda] {await OffStoreService.StopAsync(player).ConfigureAwait(false)}").ConfigureAwait(false);
                return;
            }

            if (await OffStoreService.StartAsync(player).ConfigureAwait(false) is { } error)
            {
                await player.ShowBlueMessageAsync($"[Tienda] {error}").ConfigureAwait(false);
            }
        });
    }
}
