namespace MuCustom.PlugIns.Common;

using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.NPC;

/// <summary>
/// Display names of game objects.
/// </summary>
internal static class GameNames
{
    /// <summary>
    /// Gets the name of a monster by its number.
    /// </summary>
    /// <param name="gameContext">The game context.</param>
    /// <param name="monsterNumber">The monster number.</param>
    /// <returns>The name.</returns>
    public static string Monster(IGameContext gameContext, short monsterNumber)
    {
        return gameContext.Configuration.Monsters.FirstOrDefault(m => m.Number == monsterNumber)?.Designation.ValueInNeutralLanguage
               ?? $"monstruo {monsterNumber}";
    }

    /// <summary>
    /// Gets the name of a map by its number.
    /// </summary>
    /// <param name="gameContext">The game context.</param>
    /// <param name="mapNumber">The map number.</param>
    /// <returns>The name.</returns>
    public static string Map(IGameContext gameContext, short mapNumber)
    {
        return gameContext.Configuration.Maps.FirstOrDefault(m => m.Number == mapNumber)?.Name.ValueInNeutralLanguage
               ?? $"mapa {mapNumber}";
    }

    /// <summary>
    /// Gets the name of an item, e.g. <c>Kris</c>.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The name.</returns>
    public static string Item(Item item) => item.Definition?.Name.ValueInNeutralLanguage ?? "item";

    /// <summary>
    /// Gets the player who gets the credit for a kill: the killer itself, or the owner of a summoned monster.
    /// </summary>
    /// <param name="killer">The killer.</param>
    /// <returns>The player, if any.</returns>
    public static Player? CreditedPlayer(IAttacker? killer)
    {
        return killer switch
        {
            Player player => player,
            Monster monster => monster.SummonedBy,
            _ => null,
        };
    }
}
