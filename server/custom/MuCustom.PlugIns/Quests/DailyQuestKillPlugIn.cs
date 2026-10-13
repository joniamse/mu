namespace MuCustom.PlugIns.Quests;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MuCustom.PlugIns.Common;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.GameLogic.PlugIns;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Counts monster kills for the daily quests, by monster number and map.
/// With <see cref="MuCustomSettings.DailyQuestsShareWithParty"/>, the kill also counts for the
/// party members on the same map.
/// </summary>
[PlugIn]
[Display(Name = "MuCustom: misiones diarias", Description = "Cuenta las muertes de monstruos para las misiones diarias (/diarias, /reclamar).")]
[Guid("F1E84A27-3B6C-4D09-9A5E-C2B7D0F3E816")]
public class DailyQuestKillPlugIn : IAttackableGotKilledPlugIn
{
    private const int NotifyEvery = 10;

    /// <inheritdoc />
    public async ValueTask AttackableGotKilledAsync(IAttackable killed, IAttacker? killer)
    {
        if (killed is not Monster { SummonedBy: null } monster
            || GameNames.CreditedPlayer(killer) is not { } player
            || monster.CurrentMap.Definition is not { } map)
        {
            return;
        }

        var monsterNumber = monster.Definition.Number;
        var mapNumber = map.Number;
        var receivers = MuCustomSettings.Current.DailyQuestsShareWithParty && player.Party is { } party
            ? party.PartyList.OfType<Player>().Where(p => p == player || p.CurrentMap == monster.CurrentMap).ToList()
            : [player];

        foreach (var receiver in receivers)
        {
            foreach (var (quest, progress) in DailyQuestService.CountKill(receiver, monsterNumber, mapNumber))
            {
                if (progress >= quest.RequiredCount)
                {
                    await receiver.ShowBlueMessageAsync($"[Diarias] ¡Completaste \"{quest.Title}\"! Usa /reclamar para recibir la recompensa.").ConfigureAwait(false);
                }
                else if (progress % NotifyEvery == 0)
                {
                    var target = quest.MonsterNumber is { } number ? GameNames.Monster(receiver.GameContext, number) : "monstruos";
                    await receiver.ShowBlueMessageAsync($"[Diarias] {quest.Title}: [{progress}/{quest.RequiredCount} {target}]").ConfigureAwait(false);
                }
            }
        }
    }
}
