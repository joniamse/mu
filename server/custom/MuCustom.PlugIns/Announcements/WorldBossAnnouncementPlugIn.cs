namespace MuCustom.PlugIns.Announcements;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MuCustom.PlugIns.Common;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.GameLogic.PlugIns;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Announces to the whole server who killed a world boss and on which map.
/// The bosses are configured in <see cref="MuCustomSettings.WorldBossMonsterNumbers"/>.
/// </summary>
[PlugIn]
[Display(Name = "MuCustom: anuncios de jefes de mundo", Description = "Anuncia a todo el servidor quien mato a un jefe de mundo y en que mapa.")]
[Guid("B4E92D70-5A1C-4E3F-87D6-2C0B9F5A1E48")]
public class WorldBossAnnouncementPlugIn : IAttackableGotKilledPlugIn
{
    /// <inheritdoc />
    public async ValueTask AttackableGotKilledAsync(IAttackable killed, IAttacker? killer)
    {
        var settings = MuCustomSettings.Current;
        if (!settings.AnnounceWorldBosses
            || killed is not Monster { SummonedBy: null } monster
            || !settings.WorldBossMonsterNumbers.Contains(monster.Definition.Number)
            || GameNames.CreditedPlayer(killer) is not { } player)
        {
            return;
        }

        var bossName = monster.Definition.Designation.ValueInNeutralLanguage;
        var mapName = monster.CurrentMap.Definition.Name.ValueInNeutralLanguage;
        await Announcer.AnnounceAsync(player.GameContext, $"[Sistema] ¡{player.Name} derrotó a {bossName} en {mapName}!").ConfigureAwait(false);
    }
}
