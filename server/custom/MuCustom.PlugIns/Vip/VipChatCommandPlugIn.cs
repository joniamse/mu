namespace MuCustom.PlugIns.Vip;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MuCustom.PlugIns.Common;
using MuCustom.PlugIns.Data;
using MuCustom.PlugIns.Ui;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// <c>/vip</c>: shows the membership and its benefits; <c>/vip comprar N</c> buys level N;
/// <c>/vip ui</c> sends the data of the VIP window to our client.
/// </summary>
[PlugIn]
[Display(Name = "MuCustom: /vip", Description = "Muestra y compra membresias VIP: /vip, /vip comprar <1-4>.")]
[Guid("E6B04D21-93A8-4C1F-B2E7-0A5D8C3F6B92")]
[ChatCommandHelp(Command, CharacterStatus.Normal)]
public class VipChatCommandPlugIn : IChatCommandPlugIn
{
    private const string Command = "/vip";

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
            var action = arguments.FirstOrDefault()?.ToLowerInvariant();
            switch (action)
            {
                case "ui":
                    await VipUiProtocol.SendAsync(player).ConfigureAwait(false);
                    break;
                case "comprar" or "buy" or "renovar":
                    await this.BuyAsync(player, arguments.Skip(1).FirstOrDefault(), arguments.Contains("ui")).ConfigureAwait(false);
                    break;
                default:
                    await ShowOverviewAsync(player).ConfigureAwait(false);
                    break;
            }
        });
    }

    private static async ValueTask ShowOverviewAsync(Player player)
    {
        var balance = await CashRepository.GetBalanceAsync(player.Account!.GetId()).ConfigureAwait(false);
        var (level, expiresAt) = VipService.GetEffective(player);
        var status = level.Level == 0 ? "sin membresia" : $"{level.Name} ({(level.Scope == VipScope.Account ? "toda la cuenta" : "este personaje")}) hasta {expiresAt:dd/MM/yyyy HH:mm} UTC";
        await player.ShowBlueMessageAsync($"[VIP] {status}. Fichas: {balance:N0}.").ConfigureAwait(false);
        foreach (var definition in VipLevels.All.Skip(1))
        {
            var offStore = definition.OffStore is { } d ? $"{d.TotalHours:0}h" : "ilimitada";
            await player.ShowBlueMessageAsync(
                $"{definition.Level}) {definition.Name} {definition.CostFichas} fichas/30d: exp +{definition.ExperienceBonus * 100:0}%, drop +{definition.DropBonus * 100:0}%, chaos +{definition.ChaosBonus}%, {definition.Vaults} baules, tienda offline {offStore}, helper -{definition.HelperDiscount * 100:0}%{(definition.Scope == VipScope.Account ? ", toda la cuenta" : string.Empty)}")
                .ConfigureAwait(false);
        }

        await player.ShowBlueMessageAsync("Comprar o renovar: /vip comprar <1-4>. Recargar fichas: /recargar.").ConfigureAwait(false);
    }

    private async ValueTask BuyAsync(Player player, string? levelArgument, bool fromWindow)
    {
        if (!int.TryParse(levelArgument, out var level) || level < 1 || level >= VipLevels.All.Count)
        {
            await this.ReplyAsync(player, "Uso: /vip comprar <1-4> (1 Plata, 2 Oro, 3 Platino, 4 Diamante).", fromWindow).ConfigureAwait(false);
            return;
        }

        var definition = VipLevels.Get(level);
        var result = await VipRepository.PurchaseAsync(player.Account!.GetId(), player.SelectedCharacter!.GetId(), level).ConfigureAwait(false);
        var message = result.Outcome switch
        {
            VipPurchaseOutcome.Purchased => $"¡Compraste VIP {definition.Name} por 30 dias! Saldo: {result.Balance:N0} fichas.",
            VipPurchaseOutcome.Renewed => $"Renovaste VIP {definition.Name} por 30 dias mas. Saldo: {result.Balance:N0} fichas.",
            VipPurchaseOutcome.InsufficientFichas => $"Fichas insuficientes: {definition.Name} cuesta {definition.CostFichas:N0} y tienes {result.Balance:N0}. Usa /recargar.",
            VipPurchaseOutcome.HigherLevelActive => "Ya tienes una membresia de mayor nivel activa.",
            _ => "Ese nivel VIP no existe.",
        };

        if (result.Outcome is VipPurchaseOutcome.Purchased or VipPurchaseOutcome.Renewed)
        {
            VipService.Set(player, result.Status);
            player.Logger.LogInformation("MuCustom: {Player} bought VIP {Level} ({Outcome}).", player.Name, definition.Name, result.Outcome);
        }

        await this.ReplyAsync(player, message, fromWindow).ConfigureAwait(false);
    }

    private async ValueTask ReplyAsync(Player player, string message, bool fromWindow)
    {
        if (fromWindow)
        {
            await VipUiProtocol.SendAsync(player, message).ConfigureAwait(false);
        }
        else
        {
            await player.ShowBlueMessageAsync($"[VIP] {message}").ConfigureAwait(false);
        }
    }
}
