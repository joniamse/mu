namespace MuCustom.PlugIns.Vip;

using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Runtime.InteropServices;
using MuCustom.PlugIns.Common;
using MuCustom.PlugIns.Data;
using MuCustom.PlugIns.Ui;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// <c>/recargar</c>: lists the recharge packages; <c>/recargar N</c> credits package N.
/// There is no payment: it is a simulation for testing, which can be disabled in the settings
/// (<see cref="MuCustomSettings.SimulatedRechargeEnabled"/>).
/// </summary>
[PlugIn]
[Display(Name = "MuCustom: /recargar", Description = "Recarga simulada de fichas (solo pruebas): /recargar <1-6>.")]
[Guid("4D8A1C63-2F7E-4B95-A0D3-9C6E1B4F7A28")]
[ChatCommandHelp(Command, CharacterStatus.Normal)]
public class RechargeChatCommandPlugIn : IChatCommandPlugIn
{
    private const string Command = "/recargar";

    /// <inheritdoc />
    public string Key => Command;

    /// <inheritdoc />
    public CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <inheritdoc />
    public ValueTask HandleCommandAsync(Player player, string command)
    {
        return ChatCommands.RunAsync(player, Command, async () =>
        {
            var arguments = ChatCommands.Arguments(command);
            var fromWindow = arguments.Contains("ui");
            if (!int.TryParse(arguments.FirstOrDefault(), out var packageId))
            {
                await ShowPackagesAsync(player).ConfigureAwait(false);
                return;
            }

            string message;
            if (!MuCustomSettings.Current.SimulatedRechargeEnabled)
            {
                message = "La recarga simulada esta desactivada en este servidor.";
            }
            else if (await CashRepository.CreditPackageAsync(player.Account!.GetId(), packageId).ConfigureAwait(false) is { } credited)
            {
                message = $"Recarga simulada: paquete {credited.Package.Name}, +{credited.Package.TotalFichas:N0} fichas. Saldo: {credited.Balance:N0}.";
                player.Logger.LogInformation("MuCustom: simulated recharge of {Package} for {Account}.", credited.Package.Code, player.Account!.LoginName);
            }
            else
            {
                message = "Ese paquete no existe. Usa /recargar para ver la lista.";
            }

            if (fromWindow)
            {
                await VipUiProtocol.SendAsync(player, message).ConfigureAwait(false);
            }
            else
            {
                await player.ShowBlueMessageAsync($"[Fichas] {message}").ConfigureAwait(false);
            }
        });
    }

    private static async ValueTask ShowPackagesAsync(Player player)
    {
        foreach (var package in await CashRepository.GetPackagesAsync().ConfigureAwait(false))
        {
            var bonus = package.BonusFichas > 0 ? $" (+{package.BonusFichas:N0} bonus)" : string.Empty;
            await player.ShowBlueMessageAsync($"{package.Id}) {package.Name}: USD {package.PriceUsd.ToString("0.00", CultureInfo.InvariantCulture)} = {package.BaseFichas:N0}{bonus} = {package.TotalFichas:N0} fichas").ConfigureAwait(false);
        }

        await player.ShowBlueMessageAsync("Prueba: /recargar <1-6> acredita el paquete sin pago real.").ConfigureAwait(false);
    }
}
