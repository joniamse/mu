namespace MuCustom.PlugIns.Quests;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MuCustom.PlugIns.Common;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// <c>/diarias</c>: shows the daily quests with their progress, e.g. <c>[34/50 Death Knight]</c>.
/// </summary>
[PlugIn]
[Display(Name = "MuCustom: /diarias", Description = "Muestra las misiones diarias y su progreso.")]
[Guid("6A0D3B58-C4E2-4F17-8B93-D5E1A7C0F264")]
[ChatCommandHelp(Command, CharacterStatus.Normal)]
public class DailyQuestsChatCommandPlugIn : IChatCommandPlugIn
{
    private const string Command = "/diarias";

    /// <inheritdoc />
    public string Key => Command;

    /// <inheritdoc />
    public CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <inheritdoc />
    public ValueTask HandleCommandAsync(Player player, string command)
    {
        return ChatCommands.RunAsync(player, Command, async () =>
        {
            if (DailyQuestService.GetOverview(player) is not { } overview)
            {
                await DailyQuestService.LoadAsync(player).ConfigureAwait(false);
                overview = DailyQuestService.GetOverview(player) ?? [];
            }

            if (overview.Count == 0)
            {
                await player.ShowBlueMessageAsync("[Diarias] No hay misiones disponibles para tu nivel.").ConfigureAwait(false);
                return;
            }

            var reset = DailyQuestService.TimeUntilReset;
            await player.ShowBlueMessageAsync($"[Diarias] Misiones de hoy (reinicio en {(int)reset.TotalHours}h {reset.Minutes:00}m, 00:00 UTC):").ConfigureAwait(false);
            var index = 1;
            foreach (var (quest, progress, claimed) in overview)
            {
                var target = quest.MonsterNumber is { } number ? GameNames.Monster(player.GameContext, number) : "monstruos";
                var where = quest.MapNumber is { } map ? $" en {GameNames.Map(player.GameContext, map)}" : string.Empty;
                var state = claimed ? " - RECLAMADA" : progress >= quest.RequiredCount ? " - ¡COMPLETA! /reclamar" : string.Empty;
                await player.ShowBlueMessageAsync($"{index++}) {quest.Title}: [{progress}/{quest.RequiredCount} {target}]{where}{state}").ConfigureAwait(false);
            }
        });
    }
}
