namespace MuCustom.PlugIns.Quests;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MuCustom.PlugIns.Data;
using MUnique.OpenMU.GameLogic;

/// <summary>
/// The progress of the daily quests of the online characters.
/// </summary>
/// <remarks>
/// Kills are counted in memory and written to the database periodically (and when the character
/// leaves the world or claims a reward), so a kill never waits for the database.
/// A cycle is a UTC day: when the day changes, the progress starts again from zero (lazily, at
/// the next kill or command). Claims are checked by the database, so a reward can't be claimed
/// twice in the same cycle, not even from two game servers.
/// </remarks>
public static class DailyQuestService
{
    private static readonly TimeSpan QuestReloadInterval = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<Guid, CharacterQuests> States = new();
    private static readonly SemaphoreSlim FlushLock = new(1, 1);
    private static readonly ConditionalWeakTable<Player, StrongBox<Guid>> LoadedCharacters = new();

    private static IReadOnlyList<DailyQuest> _quests = [];
    private static DateTime _nextQuestReloadUtc = DateTime.MinValue;

    /// <summary>
    /// Gets the current cycle.
    /// </summary>
    public static DateOnly CurrentPeriod => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>
    /// Gets the time until the next reset.
    /// </summary>
    public static TimeSpan TimeUntilReset => DateTime.UtcNow.Date.AddDays(1) - DateTime.UtcNow;

    /// <summary>
    /// Gets the active quests.
    /// </summary>
    public static IReadOnlyList<DailyQuest> Quests => _quests;

    /// <summary>
    /// Reloads the quest definitions if they're due.
    /// </summary>
    /// <param name="force">If set to <c>true</c>, it reloads now.</param>
    public static async ValueTask ReloadQuestsAsync(bool force = false)
    {
        if (!CustomDatabase.IsReady || (!force && DateTime.UtcNow < _nextQuestReloadUtc))
        {
            return;
        }

        _nextQuestReloadUtc = DateTime.UtcNow + QuestReloadInterval;
        _quests = await QuestRepository.GetActiveQuestsAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Loads the progress of the player's character.
    /// </summary>
    /// <param name="player">The player.</param>
    public static async ValueTask LoadAsync(Player player)
    {
        if (player.SelectedCharacter is not { } character || !CustomDatabase.IsReady)
        {
            return;
        }

        if (_quests.Count == 0)
        {
            await ReloadQuestsAsync(true).ConfigureAwait(false);
        }

        var period = CurrentPeriod;
        var stored = await QuestRepository.LoadProgressAsync(character.GetId(), period).ConfigureAwait(false);
        var state = new CharacterQuests(period);
        foreach (var (questId, (progress, claimed)) in stored)
        {
            state.Progress[questId] = progress;
            if (claimed)
            {
                state.Claimed.Add(questId);
            }
        }

        States[character.GetId()] = state;
        LoadedCharacters.AddOrUpdate(player, new StrongBox<Guid>(character.GetId()));
    }

    /// <summary>
    /// Writes the progress of the player's character and forgets it.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="logger">The logger.</param>
    public static async ValueTask UnloadAsync(Player player, ILogger logger)
    {
        // The character may already be deselected when the player leaves the world.
        if (!LoadedCharacters.TryGetValue(player, out var characterId))
        {
            return;
        }

        LoadedCharacters.Remove(player);
        await FlushAsync(logger, characterId.Value).ConfigureAwait(false);
        States.TryRemove(characterId.Value, out _);
    }

    /// <summary>
    /// Counts a kill for the player.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="monsterNumber">The number of the killed monster.</param>
    /// <param name="mapNumber">The number of the map.</param>
    /// <returns>The quests whose progress changed, with the new progress.</returns>
    public static IReadOnlyList<(DailyQuest Quest, int Progress)> CountKill(Player player, short monsterNumber, short mapNumber)
    {
        if (player.SelectedCharacter is not { } character || !States.TryGetValue(character.GetId(), out var state))
        {
            return [];
        }

        List<(DailyQuest, int)>? changed = null;
        lock (state)
        {
            state.EnsurePeriod(CurrentPeriod);
            foreach (var quest in _quests)
            {
                if (!quest.Matches(monsterNumber, mapNumber)
                    || player.Level < quest.MinimumLevel
                    || state.Claimed.Contains(quest.Id))
                {
                    continue;
                }

                var progress = state.Progress.GetValueOrDefault(quest.Id);
                if (progress >= quest.RequiredCount)
                {
                    continue;
                }

                state.Progress[quest.Id] = ++progress;
                state.Dirty.Add(quest.Id);
                (changed ??= []).Add((quest, progress));
            }
        }

        return (IReadOnlyList<(DailyQuest, int)>?)changed ?? [];
    }

    /// <summary>
    /// Gets the quests of the player with their progress.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <returns>The quests with their progress and claim state, or <c>null</c> if the progress is not loaded.</returns>
    public static IReadOnlyList<(DailyQuest Quest, int Progress, bool Claimed)>? GetOverview(Player player)
    {
        if (player.SelectedCharacter is not { } character || !States.TryGetValue(character.GetId(), out var state))
        {
            return null;
        }

        lock (state)
        {
            state.EnsurePeriod(CurrentPeriod);
            return _quests
                .Where(q => player.Level >= q.MinimumLevel)
                .Select(q => (q, Math.Min(state.Progress.GetValueOrDefault(q.Id), q.RequiredCount), state.Claimed.Contains(q.Id)))
                .ToList();
        }
    }

    /// <summary>
    /// Claims the reward of a completed quest. The database decides, so it can't be granted twice.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="quest">The quest.</param>
    /// <returns>The outcome and the fichas balance afterwards.</returns>
    public static async ValueTask<(QuestClaimOutcome Outcome, long? Balance)> ClaimAsync(Player player, DailyQuest quest)
    {
        if (player.Account is not { } account
            || player.SelectedCharacter is not { } character
            || !States.TryGetValue(character.GetId(), out var state))
        {
            return (QuestClaimOutcome.NotCompleted, null);
        }

        int progress;
        DateOnly period;
        lock (state)
        {
            state.EnsurePeriod(CurrentPeriod);
            if (state.Claimed.Contains(quest.Id))
            {
                return (QuestClaimOutcome.AlreadyClaimed, null);
            }

            progress = state.Progress.GetValueOrDefault(quest.Id);
            period = state.Period;
        }

        if (progress < quest.RequiredCount)
        {
            return (QuestClaimOutcome.NotCompleted, null);
        }

        var result = await QuestRepository.ClaimAsync(account.GetId(), character.GetId(), quest, period, progress).ConfigureAwait(false);
        if (result.Outcome is QuestClaimOutcome.Claimed or QuestClaimOutcome.AlreadyClaimed)
        {
            lock (state)
            {
                if (state.Period == period)
                {
                    state.Claimed.Add(quest.Id);
                    state.Dirty.Remove(quest.Id);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Writes the changed progress to the database.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="onlyCharacter">If set, only the progress of this character is written.</param>
    public static async ValueTask FlushAsync(ILogger logger, Guid? onlyCharacter = null)
    {
        if (!CustomDatabase.IsReady)
        {
            return;
        }

        await FlushLock.WaitAsync().ConfigureAwait(false);
        var rows = new List<(Guid, int, DateOnly, int)>();
        var taken = new List<(CharacterQuests State, DateOnly Period, List<int> QuestIds)>();
        try
        {
            foreach (var (characterId, state) in States)
            {
                if (onlyCharacter is { } only && only != characterId)
                {
                    continue;
                }

                lock (state)
                {
                    if (state.Dirty.Count == 0)
                    {
                        continue;
                    }

                    var questIds = state.Dirty.ToList();
                    state.Dirty.Clear();
                    taken.Add((state, state.Period, questIds));
                    rows.AddRange(questIds.Select(id => (characterId, id, state.Period, state.Progress.GetValueOrDefault(id))));
                }
            }

            await QuestRepository.SaveProgressAsync(rows).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MuCustom: the daily quest progress could not be saved; it is retried later.");

            // Marks them dirty again, unless the cycle changed in the meantime.
            foreach (var (state, period, questIds) in taken)
            {
                lock (state)
                {
                    if (state.Period == period)
                    {
                        state.Dirty.UnionWith(questIds);
                    }
                }
            }
        }
        finally
        {
            FlushLock.Release();
        }
    }

    private sealed class CharacterQuests
    {
        public CharacterQuests(DateOnly period)
        {
            this.Period = period;
        }

        public DateOnly Period { get; private set; }

        public Dictionary<int, int> Progress { get; } = [];

        public HashSet<int> Claimed { get; } = [];

        public HashSet<int> Dirty { get; } = [];

        // The daily reset: a new cycle starts with no progress.
        public void EnsurePeriod(DateOnly period)
        {
            if (this.Period == period)
            {
                return;
            }

            this.Period = period;
            this.Progress.Clear();
            this.Claimed.Clear();
            this.Dirty.Clear();
        }
    }
}
