namespace MuCustom.PlugIns.Quests;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MuCustom.PlugIns.Common;
using MuCustom.PlugIns.Data;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.GameLogic.Views.Inventory;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// <c>/reclamar</c>: claims the rewards (zen, experience, fichas) of all completed daily quests;
/// <c>/reclamar N</c> only of quest N of <c>/diarias</c>. A reward can be claimed once per cycle.
/// </summary>
[PlugIn]
[Display(Name = "MuCustom: /reclamar", Description = "Reclama las recompensas de las misiones diarias completadas.")]
[Guid("D3B71E06-8F5A-4C29-A6D4-0E9C2B5F8A73")]
[ChatCommandHelp(Command, CharacterStatus.Normal)]
public class ClaimRewardChatCommandPlugIn : IChatCommandPlugIn
{
    private const string Command = "/reclamar";

    /// <inheritdoc />
    public string Key => Command;

    /// <inheritdoc />
    public CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <inheritdoc />
    public ValueTask HandleCommandAsync(Player player, string command)
    {
        return ChatCommands.RunAsync(player, Command, async () =>
        {
            var overview = DailyQuestService.GetOverview(player) ?? [];
            var selection = overview.Select((entry, i) => (entry.Quest, entry.Progress, entry.Claimed, Number: i + 1)).ToList();
            if (int.TryParse(ChatCommands.Arguments(command).FirstOrDefault(), out var number))
            {
                selection = selection.Where(s => s.Number == number).ToList();
                if (selection.Count == 0)
                {
                    await player.ShowBlueMessageAsync("[Diarias] Esa mision no existe; mira la lista con /diarias.").ConfigureAwait(false);
                    return;
                }
            }

            var claimable = selection.Where(s => !s.Claimed && s.Progress >= s.Quest.RequiredCount).ToList();
            if (claimable.Count == 0)
            {
                var anyClaimed = selection.Any(s => s.Claimed);
                await player.ShowBlueMessageAsync(anyClaimed && selection.Count == 1
                    ? "[Diarias] Ya reclamaste esa recompensa hoy."
                    : "[Diarias] No tienes misiones completas para reclamar. Mira tu progreso con /diarias.").ConfigureAwait(false);
                return;
            }

            foreach (var (quest, _, _, _) in claimable)
            {
                await this.ClaimAsync(player, quest).ConfigureAwait(false);
            }
        });
    }

    private async ValueTask ClaimAsync(Player player, DailyQuest quest)
    {
        var maximumMoney = player.GameContext.Configuration.MaximumInventoryMoney;
        if ((long)player.Money + quest.RewardZen > maximumMoney)
        {
            await player.ShowBlueMessageAsync($"[Diarias] \"{quest.Title}\": no te entra el zen de la recompensa. Guarda zen en el baul y vuelve a intentar.").ConfigureAwait(false);
            return;
        }

        var (outcome, balance) = await DailyQuestService.ClaimAsync(player, quest).ConfigureAwait(false);
        switch (outcome)
        {
            case QuestClaimOutcome.AlreadyClaimed:
                await player.ShowBlueMessageAsync($"[Diarias] Ya reclamaste \"{quest.Title}\" hoy.").ConfigureAwait(false);
                return;
            case QuestClaimOutcome.NotCompleted:
                await player.ShowBlueMessageAsync($"[Diarias] \"{quest.Title}\" aun no esta completa.").ConfigureAwait(false);
                return;
        }

        if (quest.RewardZen > 0 && player.TryAddMoney(quest.RewardZen))
        {
            await player.InvokeViewPlugInAsync<IUpdateMoneyPlugIn>(p => p.UpdateMoneyAsync()).ConfigureAwait(false);
        }

        if (quest.RewardExperience > 0)
        {
            await player.AddExperienceAsync(quest.RewardExperience, null).ConfigureAwait(false);
        }

        var parts = new List<string>();
        if (quest.RewardZen > 0)
        {
            parts.Add($"{quest.RewardZen:N0} zen");
        }

        if (quest.RewardExperience > 0)
        {
            parts.Add($"{quest.RewardExperience:N0} exp");
        }

        if (quest.RewardFichas > 0)
        {
            parts.Add($"{quest.RewardFichas} fichas (saldo {balance:N0})");
        }

        player.Logger.LogInformation("MuCustom: {Player} claimed the daily quest {Quest}.", player.Name, quest.Code);
        await player.ShowBlueMessageAsync($"[Diarias] Recompensa de \"{quest.Title}\": {string.Join(", ", parts)}.").ConfigureAwait(false);
    }
}
