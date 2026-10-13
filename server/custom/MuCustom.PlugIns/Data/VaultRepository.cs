namespace MuCustom.PlugIns.Data;

using Npgsql;

/// <summary>
/// Which item storage of OpenMU is which vault of an account (<c>account_vaults</c>).
/// </summary>
public static class VaultRepository
{
    /// <summary>
    /// Gets the vaults of the account.
    /// </summary>
    /// <param name="accountId">The account identifier.</param>
    /// <returns>The item storage identifier by vault number.</returns>
    public static async ValueTask<Dictionary<int, Guid>> GetVaultsAsync(Guid accountId)
    {
        await using var connection = await CustomDatabase.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand("SELECT vault_no, item_storage_id FROM account_vaults WHERE account_id = @a;", connection);
        command.Parameters.AddWithValue("a", accountId);
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var result = new Dictionary<int, Guid>();
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            result[reader.GetInt16(0)] = reader.GetGuid(1);
        }

        return result;
    }

    /// <summary>
    /// Records which item storage is a vault.
    /// </summary>
    /// <param name="accountId">The account identifier.</param>
    /// <param name="vaultNumber">The vault number.</param>
    /// <param name="itemStorageId">The item storage identifier.</param>
    public static async ValueTask SetVaultAsync(Guid accountId, int vaultNumber, Guid itemStorageId)
    {
        await using var connection = await CustomDatabase.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO account_vaults (account_id, vault_no, item_storage_id) VALUES (@a, @n, @s)
            ON CONFLICT (account_id, vault_no) DO UPDATE SET item_storage_id = EXCLUDED.item_storage_id;
            """,
            connection);
        command.Parameters.AddWithValue("a", accountId);
        command.Parameters.AddWithValue("n", (short)vaultNumber);
        command.Parameters.AddWithValue("s", itemStorageId);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Records the vault which is in use (informative; the truth is the vault of the OpenMU account).
    /// </summary>
    /// <param name="accountId">The account identifier.</param>
    /// <param name="vaultNumber">The vault number.</param>
    public static async ValueTask SetActiveVaultAsync(Guid accountId, int vaultNumber)
    {
        await using var connection = await CustomDatabase.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO account_vault_state (account_id, active_vault_no) VALUES (@a, @n)
            ON CONFLICT (account_id) DO UPDATE SET active_vault_no = EXCLUDED.active_vault_no;
            """,
            connection);
        command.Parameters.AddWithValue("a", accountId);
        command.Parameters.AddWithValue("n", (short)vaultNumber);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
