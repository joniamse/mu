namespace MuCustom.PlugIns.Vip;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MuCustom.PlugIns.Common;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// <c>/aura 0-4</c>: shows the aura of another VIP level on the character, to try the looks;
/// <c>/aura</c> alone shows the real one again. Only while the test mode is on
/// (<see cref="MuCustomSettings.SimulatedRechargeEnabled"/>).
/// </summary>
[PlugIn]
[Display(Name = "MuCustom: /aura", Description = "Modo de prueba: muestra el aura de otro nivel VIP.")]
[Guid("7A1D4E92-8B3C-4F05-9C6E-3D2B1A0F5E84")]
[ChatCommandHelp(Command, CharacterStatus.Normal)]
public class AuraChatCommandPlugIn : IChatCommandPlugIn
{
    private const string Command = "/aura";

    /// <inheritdoc />
    public string Key => Command;

    /// <inheritdoc />
    public CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <inheritdoc />
    public ValueTask HandleCommandAsync(Player player, string command)
    {
        return ChatCommands.RunAsync(player, Command, async () =>
        {
            if (!MuCustomSettings.Current.SimulatedRechargeEnabled)
            {
                await player.ShowBlueMessageAsync("[VIP] /aura solo funciona en modo de prueba.").ConfigureAwait(false);
                return;
            }

            var argument = command.Length > Command.Length ? command[Command.Length..].Trim() : string.Empty;
            if (argument.Length == 0)
            {
                await AuraService.PreviewAsync(player, null).ConfigureAwait(false);
                await player.ShowBlueMessageAsync("[VIP] Aura: la de tu membresia. Prueba otras con /aura 1 (Plata) a /aura 4 (Diamante).").ConfigureAwait(false);
                return;
            }

            if (!int.TryParse(argument, out var level) || level < 0 || level >= VipLevels.All.Count)
            {
                await player.ShowBlueMessageAsync("[VIP] Uso: /aura 0-4 (0 sin aura, 1 Plata, 2 Oro, 3 Platino, 4 Diamante)").ConfigureAwait(false);
                return;
            }

            await AuraService.PreviewAsync(player, level).ConfigureAwait(false);
            var name = level == 0 ? "ninguna" : VipLevels.All[level].Name;
            await player.ShowBlueMessageAsync($"[VIP] Aura de prueba: {name}. /aura para volver a la tuya.").ConfigureAwait(false);
        });
    }
}
