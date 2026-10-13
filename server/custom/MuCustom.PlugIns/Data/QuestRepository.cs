namespace MuCustom.PlugIns.Data;

using MuCustom.PlugIns.Quests;
using Npgsql;
using NpgsqlTypes;

/// <summary>
/// The outcome of claiming a daily quest reward.
/// </summary>
public enum QuestClaimOutcome
{
    /// <summary>The reward was granted.</summary>
    Claimed,

    /// <summary>The objective is not reached yet.</summary>
    NotCompleted,

    /// <summary>The reward of this cycle was already claimed.</summary>
    AlreadyClaimed,
}

/// <summary>
/// The daily quests (<c>openmu_quests</c>) and their progress (<c>openmu_quest_progress</c>).
/// </summary>
public static class QuestRepository
{
    /// <summary>
    /// Gets the active quests.
    /// </summary>
    /// <returns>The quests, in their display order.</returns>
    public static async ValueTask<IReadOnlyList<DailyQuest>> GetActiveQuestsAsync()
    {
        await using var connection = await CustomDatabase.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            SELECT id, code, title, description, map_number, monster_number, required_count, min_level, reward_zen, reward_exp, reward_fichas
              FROM openmu_quests WHERE active ORDER BY sort_order, id;
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var result = new List<DailyQuest>();
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            result.Add(new DailyQuest(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt16(4),
                reader.IsDBNull(5) ? null : reader.GetInt16(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetInt32(8),
                reader.GetInt32(9),
                reader.GetInt32(10)));
        }

        return result;
    }

    /// <summary>
    /// Loads the progress of a character in a cycle.
    /// </summary>
    /// <param name="characterId">The character identifier.</param>
    /// <param name="period">The cycle (UTC date).</param>
    /// <returns>The progress and claim state by quest.</returns>
    public static async ValueTask<Dictionary<int, (int Progress, bool Claimed)>> LoadProgressAsync(Guid characterId, DateOnly period)
    {
        await using var connection = await CustomDatabase.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT quest_id, progress, claimed_at IS NOT NULL FROM openmu_quest_progress WHERE character_id = @c AND period_date = @p;",
            connection);
        command.Parameters.AddWithValue("c", characterId);
        command.Parameters.AddWithValue("p", NpgsqlDbType.Date, period);
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var result = new Dictionary<int, (int, bool)>();
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            result[reader.GetInt32(0)] = (reader.GetInt32(1), reader.GetBoolean(2));
        }

        return result;
    }

    /// <summary>
    /// Saves progress. A saved value never goes down, so a late flush can't undo newer progress.
    /// </summary>
    /// <param name="rows">The progress rows.</param>
    public static async ValueTask SaveProgressAsync(IReadOnlyCollection<(Guid CharacterId, int QuestId, DateOnly Period, int Progress)> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        await using var connection = await CustomDatabase.OpenAsync().ConfigureAwait(false);
        await using var batch = new NpgsqlBatch(connection);
        foreach (var row in rows)
        {
            var command = new NpgsqlBatchCommand(
                """
                INSERT INTO openmu_quest_progress (character_id, quest_id, period_date, progress) VALUES (@c, @q, @p, @v)
                ON CONFLICT (character_id, quest_id, period_date)
                DO UPDATE SET progress = GREATEST(openmu_quest_progress.progress, EXCLUDED.progress), updated_at = now();
                """);
            command.Parameters.AddWithValue("c", row.CharacterId);
            command.Parameters.AddWithValue("q", row.QuestId);
            command.Parameters.AddWithValue("p", NpgsqlDbType.Date, row.Period);
            command.Parameters.AddWithValue("v", row.Progress);
            batch.BatchCommands.Add(command);
        }

        await batch.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Claims the reward of a quest: marks it as claimed for the cycle and credits the fichas,
    /// in one transaction. A second claim in the same cycle is rejected by the database.
    /// </summary>
    /// <param name="accountId">The account identifier.</param>
    /// <param name="characterId">The character identifier.</param>
    /// <param name="quest">The quest.</param>
    /// <param name="period">The cycle.</param>
    /// <param name="progress">The progress known in memory; it is saved first.</param>
    /// <returns>The outcome and the fichas balance afterwards (if fichas were credited).</returns>
    public static async ValueTask<(QuestClaimOutcome Outcome, long? Balance)> ClaimAsync(Guid accountId, Guid characterId, DailyQuest quest, DateOnly period, int progress)
    {
        await using var connection = await CustomDatabase.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);

        await using (var save = new NpgsqlCommand(
            """
            INSERT INTO openmu_quest_progress (character_id, quest_id, period_date, progress) VALUES (@c, @q, @p, @v)
            ON CONFLICT (character_id, quest_id, period_date)
            DO UPDATE SET progress = GREATEST(openmu_quest_progress.progress, EXCLUDED.progress), updated_at = now();
            """,
            connection,
            transaction))
        {
            save.Parameters.AddWithValue("c", characterId);
            save.Parameters.AddWithValue("q", quest.Id);
            save.Parameters.AddWithValue("p", NpgsqlDbType.Date, period);
            save.Parameters.AddWithValue("v", progress);
            await save.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        int? claimedProgress = null;
        bool alreadyClaimed = false;
        await using (var claim = new NpgsqlCommand(
            """
            SELECT progress, claimed_at IS NOT NULL FROM openmu_quest_progress
             WHERE character_id = @c AND quest_id = @q AND period_date = @p FOR UPDATE;
            """,
            connection,
            transaction))
        {
            claim.Parameters.AddWithValue("c", characterId);
            claim.Parameters.AddWithValue("q", quest.Id);
            claim.Parameters.AddWithValue("p", NpgsqlDbType.Date, period);
            await using var reader = await claim.ExecuteReaderAsync().ConfigureAwait(false);
            if (await reader.ReadAsync().ConfigureAwait(false))
            {
                claimedProgress = reader.GetInt32(0);
                alreadyClaimed = reader.GetBoolean(1);
            }
        }

        if (alreadyClaimed)
        {
            await transaction.RollbackAsync().ConfigureAwait(false);
            return (QuestClaimOutcome.AlreadyClaimed, null);
        }

        if (claimedProgress is not { } stored || stored < quest.RequiredCount)
        {
            await transaction.RollbackAsync().ConfigureAwait(false);
            return (QuestClaimOutcome.NotCompleted, null);
        }

        await using (var mark = new NpgsqlCommand(
            "UPDATE openmu_quest_progress SET claimed_at = now(), updated_at = now() WHERE character_id = @c AND quest_id = @q AND period_date = @p;",
            connection,
            transaction))
        {
            mark.Parameters.AddWithValue("c", characterId);
            mark.Parameters.AddWithValue("q", quest.Id);
            mark.Parameters.AddWithValue("p", NpgsqlDbType.Date, period);
            await mark.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        long? balance = null;
        if (quest.RewardFichas > 0)
        {
            balance = await CashRepository.ChangeBalanceAsync(transaction, accountId, quest.RewardFichas, "daily_quest", $"{quest.Code}:{period:yyyy-MM-dd}").ConfigureAwait(false);
        }

        await transaction.CommitAsync().ConfigureAwait(false);
        return (QuestClaimOutcome.Claimed, balance);
    }
}
