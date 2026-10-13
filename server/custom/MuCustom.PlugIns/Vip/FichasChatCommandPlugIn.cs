namespace MuCustom.PlugIns.Vip;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MuCustom.PlugIns.Common;
using MuCustom.PlugIns.Data;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// <c>/fichas</c>: shows the fichas balance of the account.
/// </summary>
[PlugIn]
[Display(Name = "MuCustom: /fichas", Description = "Muestra el saldo de fichas de la cuenta.")]
[Guid("A71F3E92-5C0B-4D86-9B24-E3D6F0A8C519")]
[ChatCommandHelp(Command, CharacterStatus.Normal)]
public class FichasChatCommandPlugIn : IChatCommandPlugIn
{
    private const string Command = "/fichas";

    /// <inheritdoc />
    public string Key => Command;

    /// <inheritdoc />
    public CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <inheritdoc />
    public ValueTask HandleCommandAsync(Player player, string command)
    {
        return ChatCommands.RunAsync(player, Command, async () =>
        {
            var balance = await CashRepository.GetBalanceAsync(player.Account!.GetId()).ConfigureAwait(false);
            await player.ShowBlueMessageAsync($"[Fichas] Saldo de la cuenta: {balance:N0} fichas.").ConfigureAwait(false);
        });
    }
}
