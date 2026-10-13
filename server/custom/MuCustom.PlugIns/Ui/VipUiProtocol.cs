namespace MuCustom.PlugIns.Ui;

using System.Globalization;
using MuCustom.PlugIns.Common;
using MuCustom.PlugIns.Data;
using MuCustom.PlugIns.Vip;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Views;
using MUnique.OpenMU.Interfaces;

/// <summary>
/// Sends the data of the VIP window to our client (MuMain with the modern UI), as server messages
/// with the prefix <see cref="Prefix"/>. The client takes them out of the chat and shows the window;
/// its buttons send chat commands (<c>/vip comprar N</c>, <c>/recargar N</c>). No protocol change is needed.
/// </summary>
/// <remarks>
/// Lines (fields separated by <c>|</c>, each line below the 241 bytes of a server message):
/// <list type="bullet">
/// <item><c>#MUVIP|B</c>: begin, the client clears its data.</item>
/// <item><c>#MUVIP|S|balance|level|scope|expiresUnix|character|vault|recharge</c>: the status; scope is <c>C</c> or <c>A</c>, recharge <c>1</c> if <c>/recargar</c> works.</item>
/// <item><c>#MUVIP|L|level|name|cost|scope|exp%|drop%|chaos|vaults|offstoreHours|whilePlaying|helper%|entries</c>: a level; offstoreHours is -1 for unlimited, 0 for none.</item>
/// <item><c>#MUVIP|P|id|name|priceUsd|base|bonus|total</c>: a recharge package.</item>
/// <item><c>#MUVIP|M|text</c>: a notice for the window, e.g. the result of a purchase.</item>
/// <item><c>#MUVIP|E</c>: end, the client shows the window.</item>
/// </list>
/// </remarks>
public static class VipUiProtocol
{
    /// <summary>
    /// The prefix of the messages.
    /// </summary>
    public const string Prefix = "#MUVIP|";

    /// <summary>
    /// Sends the whole window data.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="notice">An optional notice for the window.</param>
    /// <returns>The task.</returns>
    public static async ValueTask SendAsync(Player player, string? notice = null)
    {
        if (player.Account is not { } account)
        {
            return;
        }

        var balance = await CashRepository.GetBalanceAsync(account.GetId()).ConfigureAwait(false);
        var packages = await CashRepository.GetPackagesAsync().ConfigureAwait(false);
        var activeVault = await VaultService.GetActiveVaultAsync(player).ConfigureAwait(false);
        var (level, expiresAt) = VipService.GetEffective(player);
        var expires = expiresAt is { } e ? new DateTimeOffset(e, TimeSpan.Zero).ToUnixTimeSeconds() : 0;

        await SendLineAsync(player, "B").ConfigureAwait(false);
        await SendLineAsync(player, Join("S", balance, level.Level, ScopeCode(level.Scope), expires, Clean(player.Name), activeVault, MuCustomSettings.Current.SimulatedRechargeEnabled ? 1 : 0)).ConfigureAwait(false);
        foreach (var definition in VipLevels.All.Skip(1))
        {
            var offStoreHours = definition.OffStore is { } duration ? (int)duration.TotalHours : -1;
            await SendLineAsync(
                player,
                Join(
                    "L",
                    definition.Level,
                    definition.Name,
                    definition.CostFichas,
                    ScopeCode(definition.Scope),
                    Percent(definition.ExperienceBonus),
                    Percent(definition.DropBonus),
                    definition.ChaosBonus,
                    definition.Vaults,
                    offStoreHours,
                    definition.OffStoreWhilePlaying ? 1 : 0,
                    Percent(definition.HelperDiscount),
                    definition.DailyEventEntries)).ConfigureAwait(false);
        }

        foreach (var package in packages)
        {
            await SendLineAsync(player, Join("P", package.Id, Clean(package.Name), package.PriceUsd.ToString("0.00", CultureInfo.InvariantCulture), package.BaseFichas, package.BonusFichas, package.TotalFichas)).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(notice))
        {
            await SendLineAsync(player, Join("M", Clean(notice))).ConfigureAwait(false);
        }

        await SendLineAsync(player, "E").ConfigureAwait(false);
    }

    private static string ScopeCode(VipScope scope) => scope == VipScope.Account ? "A" : "C";

    private static int Percent(double value) => (int)Math.Round(value * 100);

    private static string Clean(string? text) => (text ?? string.Empty).Replace('|', '/').Replace('\n', ' ');

    private static string Join(params object[] fields)
    {
        return string.Join('|', fields.Select(f => Convert.ToString(f, CultureInfo.InvariantCulture)));
    }

    private static ValueTask SendLineAsync(Player player, string line)
    {
        return player.InvokeViewPlugInAsync<IShowMessagePlugIn>(p => p.ShowMessageAsync(Prefix + line, MessageType.BlueNormal));
    }
}
