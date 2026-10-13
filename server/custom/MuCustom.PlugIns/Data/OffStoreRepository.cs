namespace MuCustom.PlugIns.Data;

using Npgsql;

/// <summary>
/// An open offline store (row of <c>offline_stores</c>).
/// </summary>
/// <param name="CharacterId">The character identifier.</param>
/// <param name="AccountId">The account identifier.</param>
/// <param name="LoginName">The account login name.</param>
/// <param name="CharacterName">The character name.</param>
/// <param name="StoreName">The store name.</param>
/// <param name="ServerId">The game server where the store is.</param>
/// <param name="StaysOnLogin">If the store stays open while the account plays.</param>
/// <param name="StartedAt">When the store opened.</param>
/// <param name="ExpiresAt">When it closes; <c>null</c> for never.</param>
/// <param name="SoldItems">The items sold so far.</param>
/// <param name="SoldZen">The zen earned so far.</param>
public sealed record OffStoreRecord(
    Guid CharacterId,
    Guid AccountId,
    string LoginName,
    string CharacterName,
    string StoreName,
    int ServerId,
    bool StaysOnLogin,
    DateTime StartedAt,
    DateTime? ExpiresAt,
    int SoldItems,
    long SoldZen);

/// <summary>
/// A closed offline store (row of <c>offline_store_history</c>).
/// </summary>
/// <param name="Id">The identifier.</param>
/// <param name="CharacterName">The character name.</param>
/// <param name="StartedAt">When the store opened.</param>
/// <param name="ClosedAt">When it closed.</param>
/// <param name="Reason">Why it closed.</param>
/// <param name="SoldItems">The items sold.</param>
/// <param name="SoldZen">The zen earned.</param>
public sealed record OffStoreReport(long Id, string CharacterName, DateTime StartedAt, DateTime ClosedAt, string Reason, int SoldItems, long SoldZen);

/// <summary>
/// The persistent offline stores, so they can be restored after a restart or a crash.
/// </summary>
public static class OffStoreRepository
{
    private const string Columns = "character_id, account_id, login_name, character_name, store_name, server_id, stays_on_login, started_at, expires_at, sold_items, sold_zen";

    /// <summary>
    /// Records an open store, or updates it (e.g. after it was restored).
    /// </summary>
    /// <param name="record">The store.</param>
    public static async ValueTask SaveAsync(OffStoreRecord record)
    {
        await using var connection = await CustomDatabase.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            $"""
            INSERT INTO offline_stores ({Columns})
            VALUES (@c, @a, @l, @n, @s, @srv, @stay, @start, @exp, @items, @zen)
            ON CONFLICT (character_id) DO UPDATE SET
                store_name = EXCLUDED.store_name, server_id = EXCLUDED.server_id,
                stays_on_login = EXCLUDED.stays_on_login, expires_at = EXCLUDED.expires_at;
            """,
            connection);
        command.Parameters.AddWithValue("c", record.CharacterId);
        command.Parameters.AddWithValue("a", record.AccountId);
        command.Parameters.AddWithValue("l", record.LoginName);
        command.Parameters.AddWithValue("n", record.CharacterName);
        command.Parameters.AddWithValue("s", record.StoreName);
        command.Parameters.AddWithValue("srv", (short)record.ServerId);
        command.Parameters.AddWithValue("stay", record.StaysOnLogin);
        command.Parameters.AddWithValue("start", record.StartedAt);
        command.Parameters.AddWithValue("exp", (object?)record.ExpiresAt ?? DBNull.Value);
        command.Parameters.AddWithValue("items", record.SoldItems);
        command.Parameters.AddWithValue("zen", record.SoldZen);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the stores of a game server.
    /// </summary>
    /// <param name="serverId">The game server id.</param>
    /// <returns>The stores.</returns>
    public static ValueTask<IReadOnlyList<OffStoreRecord>> GetByServerAsync(int serverId)
        => QueryAsync($"SELECT {Columns} FROM offline_stores WHERE server_id = @p;", (short)serverId);

    /// <summary>
    /// Gets the stores of an account.
    /// </summary>
    /// <param name="accountId">The account identifier.</param>
    /// <returns>The stores.</returns>
    public static ValueTask<IReadOnlyList<OffStoreRecord>> GetByAccountAsync(Guid accountId)
        => QueryAsync($"SELECT {Columns} FROM offline_stores WHERE account_id = @p;", accountId);

    /// <summary>
    /// Adds a sale to the store's totals.
    /// </summary>
    /// <param name="characterId">The character identifier.</param>
    /// <param name="zen">The zen earned by the sale.</param>
    public static async ValueTask AddSaleAsync(Guid characterId, long zen)
    {
        await using var connection = await CustomDatabase.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "UPDATE offline_stores SET sold_items = sold_items + 1, sold_zen = sold_zen + @z WHERE character_id = @c;",
            connection);
        command.Parameters.AddWithValue("c", characterId);
        command.Parameters.AddWithValue("z", zen);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Closes a store: moves it to the history, with the reason, in one transaction.
    /// </summary>
    /// <param name="characterId">The character identifier.</param>
    /// <param name="reason">Why it closed.</param>
    /// <returns><c>true</c> if there was an open store.</returns>
    public static async ValueTask<bool> CloseAsync(Guid characterId, string reason)
    {
        await using var connection = await CustomDatabase.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            WITH closed AS (DELETE FROM offline_stores WHERE character_id = @c RETURNING *)
            INSERT INTO offline_store_history (character_id, account_id, character_name, store_name, started_at, reason, sold_items, sold_zen)
            SELECT character_id, account_id, character_name, store_name, started_at, @r, sold_items, sold_zen FROM closed;
            """,
            connection);
        command.Parameters.AddWithValue("c", characterId);
        command.Parameters.AddWithValue("r", reason);
        return await command.ExecuteNonQueryAsync().ConfigureAwait(false) > 0;
    }

    /// <summary>
    /// Gets the closed stores of the account whose summary the owner didn't see yet, and marks them as seen.
    /// </summary>
    /// <param name="accountId">The account identifier.</param>
    /// <returns>The reports.</returns>
    public static async ValueTask<IReadOnlyList<OffStoreReport>> TakeUnnotifiedAsync(Guid accountId)
    {
        await using var connection = await CustomDatabase.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            UPDATE offline_store_history SET notified = true
             WHERE account_id = @a AND NOT notified
            RETURNING id, character_name, started_at, closed_at, reason, sold_items, sold_zen;
            """,
            connection);
        command.Parameters.AddWithValue("a", accountId);
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var result = new List<OffStoreReport>();
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            result.Add(new OffStoreReport(reader.GetInt64(0), reader.GetString(1), reader.GetDateTime(2), reader.GetDateTime(3), reader.GetString(4), reader.GetInt32(5), reader.GetInt64(6)));
        }

        return result.OrderBy(r => r.ClosedAt).ToList();
    }

    private static async ValueTask<IReadOnlyList<OffStoreRecord>> QueryAsync(string sql, object parameter)
    {
        await using var connection = await CustomDatabase.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("p", parameter);
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var result = new List<OffStoreRecord>();
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            result.Add(new OffStoreRecord(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt16(5),
                reader.GetBoolean(6),
                reader.GetDateTime(7),
                reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                reader.GetInt32(9),
                reader.GetInt64(10)));
        }

        return result;
    }
}
