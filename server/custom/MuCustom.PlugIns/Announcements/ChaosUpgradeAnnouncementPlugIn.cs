namespace MuCustom.PlugIns.Announcements;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MuCustom.PlugIns.Common;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlugIns;
using MUnique.OpenMU.GameLogic.Views.NPC;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Announces item upgrades of +10 to +15 at the chaos machine to the whole server:
/// the successful ones and the ones which destroyed the item.
/// </summary>
/// <remarks>
/// An upgrade is a crafting whose result adds levels to one of the required items
/// (the "+10 to +15 item combinations" of the chaos goblin).
/// </remarks>
[PlugIn]
[Display(Name = "MuCustom: anuncios de Chaos Machine", Description = "Anuncia a todo el servidor las mejoras +10 a +15: exito y destruccion.")]
[Guid("7C1A5E39-D2B4-4F86-B0E3-4A9F6C2D8E15")]
public class ChaosUpgradeAnnouncementPlugIn : IItemCraftingCompletedPlugIn
{
    private const int MaximumAnnouncedLevel = 15;

    /// <inheritdoc />
    public async ValueTask ItemCraftingCompletedAsync(Player player, ItemCraftingCompletedArgs args)
    {
        var settings = MuCustomSettings.Current;
        if (!settings.AnnounceChaosUpgrades
            || args.Crafting.SimpleCraftingSettings is not { } crafting
            || crafting.ResultItems.FirstOrDefault(r => r.AddLevel > 0 && r.Reference > 0) is not { } upgrade
            || crafting.RequiredItems.FirstOrDefault(r => r.Reference == upgrade.Reference) is not { } requirement)
        {
            return;
        }

        var input = args.InputItems.FirstOrDefault(i =>
            i.Level >= requirement.MinimumItemLevel
            && i.Level <= requirement.MaximumItemLevel
            && (requirement.PossibleItems.Count == 0 || requirement.PossibleItems.Contains(i.Definition!))
            && i.Definition?.Group is not 12 and not 14); // not jewels or other materials
        if (input is null)
        {
            return;
        }

        var targetLevel = input.Level + upgrade.AddLevel;
        if (targetLevel < settings.AnnouncedMinimumUpgradeLevel || targetLevel > MaximumAnnouncedLevel)
        {
            return;
        }

        var itemName = input.Definition?.Name.ValueInNeutralLanguage ?? "item";
        string message;
        if (args.Result == CraftingResult.Success)
        {
            message = $"[Sistema] ¡El jugador {player.Name} mejoró con éxito su {itemName} a +{targetLevel}!";
        }
        else if (args.Result == CraftingResult.Failed && !args.RemainingItems.Contains(input.Item))
        {
            message = $"[Sistema] La Chaos Machine ha destruido el {itemName} de {player.Name} al intentar mejorarlo a +{targetLevel}.";
        }
        else
        {
            return;
        }

        await Announcer.AnnounceAsync(player.GameContext, message).ConfigureAwait(false);
    }
}
