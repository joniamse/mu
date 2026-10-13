namespace MuCustom.PlugIns.Vip;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MuCustom.PlugIns.Common;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// <c>/baul N</c>: switches to vault N (1 to 5, as many as the membership allows).
/// </summary>
[PlugIn]
[Display(Name = "MuCustom: /baul", Description = "Cambia entre los baules de la cuenta: /baul <1-5>.")]
[Guid("5B2D9F47-E1A6-4C38-8F05-7A3C6E2B9D14")]
[ChatCommandHelp(Command, CharacterStatus.Normal)]
public class VaultChatCommandPlugIn : IChatCommandPlugIn
{
    private const string Command = "/baul";

    /// <inheritdoc />
    public string Key => Command;

    /// <inheritdoc />
    public CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <inheritdoc />
    public ValueTask HandleCommandAsync(Player player, string command)
    {
        return ChatCommands.RunAsync(player, Command, async () =>
        {
            var allowed = VipService.GetLevel(player).Vaults;
            if (!int.TryParse(ChatCommands.Arguments(command).FirstOrDefault(), out var target))
            {
                var active = await VaultService.GetActiveVaultAsync(player).ConfigureAwait(false);
                await player.ShowBlueMessageAsync($"[Baul] Usas el baul {active}. Tu membresia permite {allowed}. Uso: /baul <1-{allowed}>.").ConfigureAwait(false);
                return;
            }

            var message = await VaultService.SwitchAsync(player, target).ConfigureAwait(false);
            await player.ShowBlueMessageAsync($"[Baul] {message}").ConfigureAwait(false);
        });
    }
}
