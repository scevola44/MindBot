using MindBot.Core.Durability;
using MindBot.Core.Options;
using MindBot.Core.Web;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MindBot.Infrastructure.State;

public static class StateServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SQLite durability store. A <em>factory</em> rather than a scoped DbContext:
    /// the ingest loop and the drain worker are independent long-running loops that write
    /// concurrently, and a DbContext is not thread-safe.
    /// </summary>
    public static IServiceCollection AddMindBotState(this IServiceCollection services)
    {
        services.AddDbContextFactory<MindBotDbContext>((serviceProvider, options) =>
        {
            var stateOptions = serviceProvider.GetRequiredService<IOptions<StateOptions>>().Value;
            options.UseSqlite(BuildConnectionString(stateOptions.DatabasePath));
        });

        // One instance behind both interfaces: Telegram and web ingest share the same transaction type.
        services.AddSingleton<EfIngestUnitOfWorkFactory>();
        services.AddSingleton<IIngestUnitOfWorkFactory>(sp => sp.GetRequiredService<EfIngestUnitOfWorkFactory>());
        services.AddSingleton<IWebIngestUnitOfWorkFactory>(sp => sp.GetRequiredService<EfIngestUnitOfWorkFactory>());
        services.AddSingleton<IWebTranscript, EfWebTranscript>();
        services.AddSingleton<IDeviceTokenStore, EfDeviceTokenStore>();
        services.AddSingleton<IWriteJobQueue, EfWriteJobQueue>();
        services.AddSingleton<IBackgroundJobQueue, EfBackgroundJobQueue>();
        services.AddSingleton<IRepositoryStateStore, EfRepositoryStateStore>();
        services.AddSingleton<StateMaintenance>();

        return services;
    }

    public static string BuildConnectionString(string databasePath) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
            // Microsoft.Data.Sqlite retries SQLITE_BUSY for this long, which is what keeps the
            // ingest and drain connections from tripping over each other under WAL.
            DefaultTimeout = 30,
        }.ToString();
}
