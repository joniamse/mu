namespace MuCustom.PlugIns.Vip;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MuCustom.PlugIns.Common;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// <c>/entradas</c>: gives the daily Blood Castle and Devil Square entries of the membership.
/// </summary>
[PlugIn]
[Display(Name = "MuCustom: /entradas", Description = "Entrega las entradas diarias VIP a Blood Castle y Devil Square.")]
[Guid("9E3C7A15-6D2F-4B80-A4E9-1F5B8D0C2E67")]
[ChatCommandHelp(Command, CharacterStatus.Normal)]
public class EventEntriesChatCommandPlugIn : IChatCommandPlugIn
{
    private const string Command = "/entradas";

    /// <inheritdoc />
    public string Key => Command;

    /// <inheritdoc />
    public CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <inheritdoc />
    public ValueTask HandleCommandAsync(Player player, string command)
    {
        return ChatCommands.RunAsync(player, Command, async () =>
        {
            var message = await EventEntryService.ClaimAsync(player).ConfigureAwait(false);
            await player.ShowBlueMessageAsync($"[VIP] {message}").ConfigureAwait(false);
        });
    }
}
