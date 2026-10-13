namespace MuCustom.PlugIns.Data;

using MuCustom.PlugIns.Vip;
using Npgsql;

/// <summary>
/// The memberships (<c>account_vip_status</c> and <c>character_vip_status</c>).
/// </summary>
public static class VipRepository
{
    /// <summary>
    /// Gets the memberships of the character and its account.
    /// </summary>
    /// <param name="accountId">The account identifier.</param>
    /// <param name="characterId">The character identifier.</param>
    /// <returns>The status.</returns>
    public static async ValueTask<VipStatus> GetStatusAsync(Guid accountId, Guid characterId)
    {
        await using var connection = await CustomDatabase.OpenAsync().ConfigureAwait(false);
        return await GetStatusAsync(connection, null, accountId, characterId).ConfigureAwait(false);
    }

    /// <summary>
    /// Buys a membership for <see cref="VipLevels.Duration"/>: validates the balance, deducts it and
    /// starts or extends the membership, all in one transaction.
    /// Levels with <see cref="VipScope.Character"/> apply to the given character, the others to the account.
    /// </summary>
    /// <param name="accountId">The account identifier.</param>
    /// <param name="characterId">The character identifier.</param>
    /// <param name="level">The level to buy.</param>
    /// <returns>The result.</returns>
    public static async ValueTask<VipPurchaseResult> PurchaseAsync(Guid accountId, Guid characterId, int level)
    {
        if (level < 1 || level >= VipLevels.All.Count)
        {
            return new VipPurchaseResult(VipPurchaseOutcome.InvalidLevel, 0, VipStatus.None);
        }

        var definition = VipLevels.Get(level);
        await using var connection = await CustomDatabase.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);

        // Locks the balance row of the account, so that purchases of the same account run one after another.
        await using (var ensure = new NpgsqlCommand("INSERT INTO account_cash_currency (account_id) VALUES (@a) ON CONFLICT (account_id) DO NOTHING;", connection, transaction))
        {
            ensure.Parameters.AddWithValue("a", accountId);
            await ensure.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        long balance;
        await using (var lockBalance = new NpgsqlCommand("SELECT fichas_balance FROM account_cash_currency WHERE account_id = @a FOR UPDATE;", connection, transaction))
        {
            lockBalance.Parameters.AddWithValue("a", accountId);
            balance = (long)(await lockBalance.ExecuteScalarAsync().ConfigureAwait(false))!;
        }

        var now = DateTime.UtcNow;
        var status = await GetStatusAsync(connection, transaction, accountId, characterId).ConfigureAwait(false);
        var (effective, _) = status.GetEffective(now);
        if (effective.Level > level)
        {
            await transaction.RollbackAsync().ConfigureAwait(false);
            return new VipPurchaseResult(VipPurchaseOutcome.HigherLevelActive, balance, status);
        }

        var (currentLevel, currentExpiry) = definition.Scope == VipScope.Account
            ? (status.AccountLevel, status.AccountExpiresAt)
            : (status.CharacterLevel, status.CharacterExpiresAt);
        var renew = currentLevel == level && currentExpiry > now;
        var expiresAt = (renew ? currentExpiry!.Value : now) + VipLevels.Duration;

        var newBalance = await CashRepository.ChangeBalanceAsync(transaction, accountId, -definition.CostFichas, "vip_purchase", $"{definition.Name}:{characterId}").ConfigureAwait(false);
        if (newBalance is null)
        {
            await transaction.RollbackAsync().ConfigureAwait(false);
            return new VipPurchaseResult(VipPurchaseOutcome.InsufficientFichas, balance, status);
        }

        var sql = definition.Scope == VipScope.Account
            ? """
              INSERT INTO account_vip_status (account_id, vip_level, expires_at) VALUES (@a, @l, @e)
              ON CONFLICT (account_id) DO UPDATE SET vip_level = EXCLUDED.vip_level, expires_at = EXCLUDED.expires_at, updated_at = now();
              """
            : """
              INSERT INTO character_vip_status (character_id, account_id, vip_level, expires_at) VALUES (@c, @a, @l, @e)
              ON CONFLICT (character_id) DO UPDATE SET vip_level = EXCLUDED.vip_level, expires_at = EXCLUDED.expires_at, updated_at = now();
              """;
        await using (var upsert = new NpgsqlCommand(sql, connection, transaction))
        {
            upsert.Parameters.AddWithValue("a", accountId);
            upsert.Parameters.AddWithValue("c", characterId);
            upsert.Parameters.AddWithValue("l", (short)level);
            upsert.Parameters.AddWithValue("e", expiresAt);
            await upsert.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        var newStatus = await GetStatusAsync(connection, transaction, accountId, characterId).ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return new VipPurchaseResult(renew ? VipPurchaseOutcome.Renewed : VipPurchaseOutcome.Purchased, newBalance.Value, newStatus);
    }

    private static async ValueTask<VipStatus> GetStatusAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid accountId, Guid characterId)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT (SELECT vip_level FROM account_vip_status WHERE account_id = @a),
                   (SELECT expires_at FROM account_vip_status WHERE account_id = @a),
                   (SELECT vip_level FROM character_vip_status WHERE character_id = @c),
                   (SELECT expires_at FROM character_vip_status WHERE character_id = @c);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("a", accountId);
        command.Parameters.AddWithValue("c", characterId);
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        await reader.ReadAsync().ConfigureAwait(false);
        return new VipStatus(
            reader.IsDBNull(0) ? 0 : reader.GetInt16(0),
            reader.IsDBNull(1) ? null : reader.GetDateTime(1),
            reader.IsDBNull(2) ? 0 : reader.GetInt16(2),
            reader.IsDBNull(3) ? null : reader.GetDateTime(3));
    }
}
