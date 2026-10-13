namespace MuCustom.PlugIns.Quests;

/// <summary>
/// A daily quest (row of <c>openmu_quests</c>): kill a number of monsters.
/// </summary>
/// <param name="Id">The identifier.</param>
/// <param name="Code">The code.</param>
/// <param name="Title">The title.</param>
/// <param name="Description">The description.</param>
/// <param name="MapNumber">The map where the kills count; <c>null</c> for any map.</param>
/// <param name="MonsterNumber">The monster which has to be killed; <c>null</c> for any monster.</param>
/// <param name="RequiredCount">The number of kills.</param>
/// <param name="MinimumLevel">The minimum character level.</param>
/// <param name="RewardZen">The zen reward.</param>
/// <param name="RewardExperience">The experience reward.</param>
/// <param name="RewardFichas">The fichas reward.</param>
public sealed record DailyQuest(
    int Id,
    string Code,
    string Title,
    string Description,
    short? MapNumber,
    short? MonsterNumber,
    int RequiredCount,
    int MinimumLevel,
    int RewardZen,
    int RewardExperience,
    int RewardFichas)
{
    /// <summary>
    /// Determines whether a kill counts for this quest.
    /// </summary>
    /// <param name="monsterNumber">The number of the killed monster.</param>
    /// <param name="mapNumber">The number of the map.</param>
    /// <returns><c>true</c> if it counts.</returns>
    public bool Matches(short monsterNumber, short mapNumber)
    {
        return (this.MonsterNumber is null || this.MonsterNumber == monsterNumber)
               && (this.MapNumber is null || this.MapNumber == mapNumber);
    }
}
