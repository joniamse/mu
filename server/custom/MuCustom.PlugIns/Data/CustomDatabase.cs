namespace MuCustom.PlugIns.Data;

using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.Persistence.EntityFramework;
using Npgsql;

/// <summary>
/// Access to our own tables in the OpenMU database, with a versioned migration runner.
/// </summary>
/// <remarks>
/// The connection is the same one OpenMU uses for its admin context and follows the same
/// environment variables (<c>DB_HOST</c>, <c>DB_ADMIN_USER</c>, <c>DB_ADMIN_PW</c>).
/// <c>MUCUSTOM_CONNECTION_STRING</c> replaces it completely.
/// The migrations are the embedded <c>Data/Migrations/NNNN_name.sql</c> files; each one runs once,
/// in its own transaction, and is recorded in <c>mucustom_schema_migrations</c>.
/// </remarks>
public static partial class CustomDatabase
{
    private const string DefaultConnectionString = "Server=localhost;Port=5432;User Id=postgres;Password=admin;Database=openmu;Command Timeout=60;";

    private static readonly SemaphoreSlim MigrationLock = new(1, 1);
    private static readonly Lazy<NpgsqlDataSource> LazyDataSource = new(CreateDataSource);

    private static volatile bool _migrated;
    private static DateTime _nextMigrationAttemptUtc = DateTime.MinValue;

    /// <summary>
    /// Gets a value indicating whether the migrations have been applied, so the tables can be used.
    /// </summary>
    public static bool IsReady => _migrated;

    /// <summary>
    /// Opens a connection. Throws if the database is not ready yet.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The opened connection.</returns>
    public static async ValueTask<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        if (!_migrated)
        {
            throw new InvalidOperationException("The MuCustom database tables are not ready yet.");
        }

        return await LazyDataSource.Value.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies the pending migrations. Safe to call often: after the first success it returns at once,
    /// after a failure (e.g. the database is still being created) it waits a bit before retrying.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <returns><c>true</c> when the database is ready.</returns>
    public static async ValueTask<bool> EnsureMigratedAsync(ILogger logger)
    {
        if (_migrated)
        {
            return true;
        }

        if (DateTime.UtcNow < _nextMigrationAttemptUtc || !await MigrationLock.WaitAsync(0).ConfigureAwait(false))
        {
            return false;
        }

        try
        {
            if (_migrated)
            {
                return true;
            }

            await MigrateAsync(logger).ConfigureAwait(false);
            _migrated = true;
            return true;
        }
        catch (Exception ex)
        {
            _nextMigrationAttemptUtc = DateTime.UtcNow.AddSeconds(30);
            logger.LogWarning(ex, "MuCustom: the database migrations could not be applied yet; retrying in 30 seconds.");
            return false;
        }
        finally
        {
            MigrationLock.Release();
        }
    }

    private static NpgsqlDataSource CreateDataSource()
    {
        var connectionString = Environment.GetEnvironmentVariable("MUCUSTOM_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = DatabaseConnectionStringHelper.ApplyEnvironmentVariables(DefaultConnectionString);
        }

        return NpgsqlDataSource.Create(connectionString);
    }

    private static async Task MigrateAsync(ILogger logger)
    {
        await using var connection = await LazyDataSource.Value.OpenConnectionAsync().ConfigureAwait(false);
        await using (var create = new NpgsqlCommand(
            """
            CREATE TABLE IF NOT EXISTS mucustom_schema_migrations (
                version    integer     PRIMARY KEY,
                name       text        NOT NULL,
                applied_at timestamptz NOT NULL DEFAULT now()
            );
            """,
            connection))
        {
            await create.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        foreach (var (version, name, sql) in LoadMigrations())
        {
            await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);

            // Several game servers may start at once; the lock serializes them.
            await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_xact_lock(74210001);", connection, transaction))
            {
                await lockCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            await using (var check = new NpgsqlCommand("SELECT 1 FROM mucustom_schema_migrations WHERE version = @v;", connection, transaction))
            {
                check.Parameters.AddWithValue("v", version);
                if (await check.ExecuteScalarAsync().ConfigureAwait(false) is not null)
                {
                    await transaction.RollbackAsync().ConfigureAwait(false);
                    continue;
                }
            }

            await using (var migration = new NpgsqlCommand(sql, connection, transaction))
            {
                await migration.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            await using (var record = new NpgsqlCommand("INSERT INTO mucustom_schema_migrations (version, name) VALUES (@v, @n);", connection, transaction))
            {
                record.Parameters.AddWithValue("v", version);
                record.Parameters.AddWithValue("n", name);
                await record.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            await transaction.CommitAsync().ConfigureAwait(false);
            logger.LogInformation("MuCustom: applied database migration {Version} ({Name}).", version, name);
        }
    }

    private static IEnumerable<(int Version, string Name, string Sql)> LoadMigrations()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var migrations = new List<(int, string, string)>();
        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            var match = MigrationNameRegex().Match(resourceName);
            if (!match.Success)
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream);
            migrations.Add((int.Parse(match.Groups["version"].Value), match.Groups["name"].Value, reader.ReadToEnd()));
        }

        return migrations.OrderBy(m => m.Item1);
    }

    [GeneratedRegex(@"Migrations\.(?<version>\d{4})_(?<name>[a-z0-9_]+)\.sql$")]
    private static partial Regex MigrationNameRegex();
}
