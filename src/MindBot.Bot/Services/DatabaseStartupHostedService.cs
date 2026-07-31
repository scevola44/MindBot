using MindBot.Core.Options;
using MindBot.Infrastructure.State;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MindBot.Bot.Services;

/// <summary>
/// Brings the durability database up before anything else runs: creates the directory, applies
/// migrations, switches the file to WAL, and prunes expired rows. Registered first so a broken
/// state volume fails startup visibly rather than surfacing as lost notes later.
/// </summary>
public sealed class DatabaseStartupHostedService(
    IDbContextFactory<MindBotDbContext> dbContextFactory,
    StateMaintenance stateMaintenance,
    IOptions<StateOptions> stateOptions,
    ILogger<DatabaseStartupHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var databasePath = stateOptions.Value.DatabasePath;

        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using (var db = await dbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            await db.Database.MigrateAsync(cancellationToken);
        }

        await EnableWriteAheadLoggingAsync(databasePath, cancellationToken);
        await stateMaintenance.PruneAsync(cancellationToken);

        logger.LogInformation("Durability database ready at {DatabasePath}.", databasePath);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// WAL lets the ingest loop and the drain worker write on separate connections without
    /// blocking each other. It is a durable property of the database file, so setting it once
    /// after migration is enough.
    /// </summary>
    private static async Task EnableWriteAheadLoggingAsync(string databasePath, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(StateServiceCollectionExtensions.BuildConnectionString(databasePath));
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
