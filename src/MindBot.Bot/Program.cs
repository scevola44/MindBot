using MindBot.Bot.Logging;
using MindBot.Bot.Services;
using MindBot.Core.Durability;
using MindBot.Core.Git;
using MindBot.Core.Health;
using MindBot.Core.Ingest;
using MindBot.Core.Logging;
using MindBot.Core.Notes;
using MindBot.Core.Notifications;
using MindBot.Core.Options;
using MindBot.Core.Sync;
using MindBot.Infrastructure.Git;
using MindBot.Infrastructure.State;
using MindBot.Infrastructure.Vault;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Telegram.Bot;

var builder = WebApplication.CreateBuilder(args);

// The health endpoint is only ever reached by the container's own healthcheck, so it binds
// in-container and is never published to the host.
builder.WebHost.UseUrls("http://0.0.0.0:8080");

builder.Services.AddOptionsWithValidateOnStart<TelegramOptions, TelegramOptionsValidator>()
    .BindConfiguration(TelegramOptions.SectionName);
builder.Services.AddOptionsWithValidateOnStart<GitOptions, GitOptionsValidator>()
    .BindConfiguration(GitOptions.SectionName);
builder.Services.AddOptionsWithValidateOnStart<VaultOptions, VaultOptionsValidator>()
    .BindConfiguration(VaultOptions.SectionName);
builder.Services.AddOptionsWithValidateOnStart<StateOptions, StateOptionsValidator>()
    .BindConfiguration(StateOptions.SectionName);

// Read straight from configuration rather than IOptions: the log formatter is constructed while
// the logging stack is being built, and resolving IOptions there would trigger options validation
// from inside logging.
builder.Services.AddSingleton(new SecretRedactor(builder.Configuration[$"{TelegramOptions.SectionName}:BotToken"]));

builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.FormatterName = RedactingConsoleFormatter.FormatterName);
builder.Logging.AddConsoleFormatter<RedactingConsoleFormatter, ConsoleFormatterOptions>();

// Finish the in-flight batch on SIGTERM rather than truncating a push mid-flight. Nothing is
// buffered in memory, so this only needs to outlast one git round trip.
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(60));

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddMindBotState();

builder.Services.AddSingleton<IGitService, GitService>();
builder.Services.AddSingleton<IVaultWriter, VaultNoteWriter>();
builder.Services.AddSingleton<NotePlanner>();
builder.Services.AddSingleton<MessageRouter>();
builder.Services.AddSingleton<TelegramAuthorization>();
builder.Services.AddSingleton<IOperatorNotifier, TelegramOperatorNotifier>();
builder.Services.AddSingleton<HealthSnapshot>();
builder.Services.AddSingleton<WriteJobSignal>();
builder.Services.AddSingleton<VaultSyncService>();

builder.Services
    .AddHttpClient("telegram_bot_client")
    .AddTypedClient<ITelegramBotClient>((httpClient, sp) =>
    {
        var token = sp.GetRequiredService<IOptions<TelegramOptions>>().Value.BotToken;
        return new TelegramBotClient(token, httpClient);
    });

// Registration order matters. The database must exist before anything reads it, the git self-check
// must pass before work is attempted, and the sync worker must drain the previous run's backlog
// before the poller starts accepting new updates.
builder.Services.AddHostedService<DatabaseStartupHostedService>();
builder.Services.AddHostedService<GitStartupCheckHostedService>();
builder.Services.AddHostedService<VaultSyncHostedService>();
builder.Services.AddHostedService<TelegramPollingHostedService>();

var app = builder.Build();

var processStartedAt = app.Services.GetRequiredService<TimeProvider>().GetUtcNow();

app.MapGet("/health", async (
    HealthSnapshot snapshot,
    IWriteJobQueue jobQueue,
    TimeProvider timeProvider,
    CancellationToken cancellationToken) =>
{
    var report = snapshot.Read();
    var now = timeProvider.GetUtcNow();

    // A degraded git state (un-pushed commits, dirty tree) is reported but does NOT fail the
    // check: a remote that is down for an hour is a condition this bot is designed to ride out,
    // not a broken container. Only a stalled poller or an unreachable state database is fatal.
    bool stateStoreReachable;
    try
    {
        await jobQueue.GetPendingCountAsync(cancellationToken);
        stateStoreReachable = true;
    }
    catch (Exception)
    {
        stateStoreReachable = false;
    }

    // 3x the 30s long-poll timeout: one missed cycle is normal, three is not.
    var lastPollOrStartup = report.LastSuccessfulPollAt ?? processStartedAt;
    var pollStalled = now - lastPollOrStartup > TimeSpan.FromSeconds(180);

    var healthy = stateStoreReachable && !pollStalled;

    var payload = new
    {
        status = healthy ? "healthy" : "unhealthy",
        degraded = report.Degraded,
        lastSuccessfulPollUtc = report.LastSuccessfulPollAt,
        lastSuccessfulPushUtc = report.LastSuccessfulPushAt,
        queueDepth = report.QueueDepth,
        unpushedCommitCount = report.UnpushedCommitCount,
        workingTreeDirty = report.WorkingTreeDirty,
        lastClassification = report.LastClassification,
        stateStoreReachable,
        pollStalled,
    };

    return Results.Json(payload, statusCode: healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
});

app.Run();
