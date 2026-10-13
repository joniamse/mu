namespace MuCustom.PlugIns.Data;

using Npgsql;

/// <summary>
/// The fichas balance of the accounts and the recharge packages.
/// </summary>
public static class CashRepository
{
    /// <summary>
    /// Makes sure the account has a balance row, so it can also be edited by SQL.
    /// </summary>
    /// <param name="accountId">The account identifier.</param>
    public static async ValueTask EnsureAccountAsync(Guid accountId)
    {
        await using var connection = await CustomDatabase.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand("INSERT INTO account_cash_currency (account_id) VALUES (@a) ON CONFLICT (account_id) DO NOTHING;", connection);
        command.Parameters.AddWithValue("a", accountId);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the fichas balance of the account.
    /// </summary>
    /// <param name="accountId">The account identifier.</param>
    /// <returns>The balance.</returns>
    public static async ValueTask<long> GetBalanceAsync(Guid accountId)
    {
        await using var connection = await CustomDatabase.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand("SELECT fichas_balance FROM account_cash_currency WHERE account_id = @a;", connection);
        command.Parameters.AddWithValue("a", accountId);
        return await command.ExecuteScalarAsync().ConfigureAwait(false) is long balance ? balance : 0;
    }

    /// <summary>
    /// Gets the active recharge packages.
    /// </summary>
    /// <returns>The packages, ordered by price.</returns>
    public static async ValueTask<IReadOnlyList<RechargePackage>> GetPackagesAsync()
    {
        await using var connection = await CustomDatabase.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT id, code, name, price_usd, base_fichas, bonus_fichas, total_fichas FROM shop_recharge_packages WHERE active ORDER BY price_usd, id;",
            connection);
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var result = new List<RechargePackage>();
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            result.Add(new RechargePackage(reader.GetInt16(0), reader.GetString(1), reader.GetString(2), reader.GetDecimal(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6)));
        }

        return result;
    }

    /// <summary>
    /// Adds (or with a negative amount, removes) fichas within the given transaction and records it in the ledger.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="accountId">The account identifier.</param>
    /// <param name="delta">The amount to add; negative to remove.</param>
    /// <param name="reason">The reason, e.g. <c>recharge</c>.</param>
    /// <param name="reference">An optional reference, e.g. the package code.</param>
    /// <returns>The new balance, or <c>null</c> if the balance is too low.</returns>
    public static async ValueTask<long?> ChangeBalanceAsync(NpgsqlTransaction transaction, Guid accountId, long delta, string reason, string? reference)
    {
        var connection = transaction.Connection!;
        await using (var ensure = new NpgsqlCommand("INSERT INTO account_cash_currency (account_id) VALUES (@a) ON CONFLICT (account_id) DO NOTHING;", connection, transaction))
        {
            ensure.Parameters.AddWithValue("a", accountId);
            await ensure.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        long balance;
        await using (var update = new NpgsqlCommand(
            """
            UPDATE account_cash_currency
               SET fichas_balance = fichas_balance + @d, updated_at = now()
             WHERE account_id = @a AND fichas_balance + @d >= 0
            RETURNING fichas_balance;
            """,
            connection,
            transaction))
        {
            update.Parameters.AddWithValue("a", accountId);
            update.Parameters.AddWithValue("d", delta);
            if (await update.ExecuteScalarAsync().ConfigureAwait(false) is not long newBalance)
            {
                return null;
            }

            balance = newBalance;
        }

        await using (var ledger = new NpgsqlCommand(
            "INSERT INTO fichas_ledger (account_id, delta, balance_after, reason, reference) VALUES (@a, @d, @b, @r, @ref);",
            connection,
            transaction))
        {
            ledger.Parameters.AddWithValue("a", accountId);
            ledger.Parameters.AddWithValue("d", delta);
            ledger.Parameters.AddWithValue("b", balance);
            ledger.Parameters.AddWithValue("r", reason);
            ledger.Parameters.AddWithValue("ref", (object?)reference ?? DBNull.Value);
            await ledger.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        return balance;
    }

    /// <summary>
    /// Credits a recharge package (simulated payment).
    /// </summary>
    /// <param name="accountId">The account identifier.</param>
    /// <param name="packageId">The package identifier.</param>
    /// <returns>The package and the new balance, or <c>null</c> if the package doesn't exist.</returns>
    public static async ValueTask<(RechargePackage Package, long Balance)?> CreditPackageAsync(Guid accountId, int packageId)
    {
        var package = (await GetPackagesAsync().ConfigureAwait(false)).FirstOrDefault(p => p.Id == packageId);
        if (package is null)
        {
            return null;
        }

        await using var connection = await CustomDatabase.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        var balance = await ChangeBalanceAsync(transaction, accountId, package.TotalFichas, "recharge_simulated", package.Code).ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return (package, balance ?? 0);
    }
}
