using MindBot.Bot.Logging;
using MindBot.Bot.Services;
using MindBot.Bot.Web;
using MindBot.Core.Commands;
using MindBot.Core.Durability;
using MindBot.Core.Git;
using MindBot.Core.Health;
using MindBot.Core.Ingest;
using MindBot.Core.Logging;
using MindBot.Core.Notes;
using MindBot.Core.Notifications;
using MindBot.Core.Options;
using MindBot.Core.Sync;
using MindBot.Core.YouTube;
using MindBot.Infrastructure.Git;
using MindBot.Infrastructure.N8n;
using MindBot.Infrastructure.State;
using MindBot.Infrastructure.Vault;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Telegram.Bot;

// `docker compose exec mindbot dotnet MindBot.Bot.dll device add <name>`: manage web devices
// without starting the bot.
if (args is [DeviceCli.Verb, ..])
{
    return await DeviceCli.RunAsync(args[1..], Console.Out, Console.Error);
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptionsWithValidateOnStart<TelegramOptions, TelegramOptionsValidator>()
    .BindConfiguration(TelegramOptions.SectionName);
builder.Services.AddOptionsWithValidateOnStart<WebOptions, WebOptionsValidator>()
    .BindConfiguration(WebOptions.SectionName);
builder.Services.AddOptionsWithValidateOnStart<GitOptions, GitOptionsValidator>()
    .BindConfiguration(GitOptions.SectionName);
builder.Services.AddOptionsWithValidateOnStart<VaultOptions, VaultOptionsValidator>()
    .BindConfiguration(VaultOptions.SectionName);
builder.Services.AddOptionsWithValidateOnStart<StateOptions, StateOptionsValidator>()
    .BindConfiguration(StateOptions.SectionName);
builder.Services.AddOptionsWithValidateOnStart<N8nOptions, N8nOptionsValidator>()
    .BindConfiguration(N8nOptions.SectionName);

// Which channels exist decides what gets registered, so it is read before the container is
// built. The validators above still run at startup and report anything wrong with these values.
var telegram = builder.Configuration.GetSection(TelegramOptions.SectionName).Get<TelegramOptions>() ?? new TelegramOptions();
var web = builder.Configuration.GetSection(WebOptions.SectionName).Get<WebOptions>() ?? new WebOptions();
var webPort = web.Enabled && web.Port is > 0 and <= 65535 and not WebOptions.HealthPort ? web.Port : (int?)null;

// The health port is only ever reached by the container's own healthcheck and is never
// published. The web port is published to the host's loopback only and reached over the VPN.
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.ListenAnyIP(WebOptions.HealthPort);
    if (webPort is not null)
    {
        kestrel.ListenAnyIP(webPort.Value);
    }
});

// Read straight from configuration rather than IOptions: the log formatter is constructed while
// the logging stack is being built, and resolving IOptions there would trigger options validation
// from inside logging.
builder.Services.AddSingleton(new SecretRedactor(telegram.BotToken));

builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.FormatterName = RedactingConsoleFormatter.FormatterName);
builder.Logging.AddConsoleFormatter<RedactingConsoleFormatter, ConsoleFormatterOptions>();

// Finish the in-flight batch on SIGTERM rather than truncating a push mid-flight. Nothing is
// buffered in memory, so this only needs to outlast one git round trip.
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(60));

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddMindBotState();
builder.Services.AddMindBotCommands();

builder.Services.AddSingleton<IGitService, GitService>();
builder.Services.AddSingleton<IVaultWriter, VaultNoteWriter>();
builder.Services.AddSingleton<NotePlanner>();
builder.Services.AddSingleton<MessageRouter>();
builder.Services.AddSingleton<IChatReplySender, RoutingChatReplySender>();
builder.Services.AddSingleton<IOperatorNotifier, LatchingOperatorNotifier>();
builder.Services.AddSingleton<HealthSnapshot>();
builder.Services.AddSingleton<WriteJobSignal>();
builder.Services.AddSingleton<BackgroundJobSignal>();
builder.Services.AddSingleton<VaultSyncService>();
builder.Services.AddSingleton<YouTubeSummaryPipeline>();
builder.Services.AddSingleton<YouTubeSummaryJobRunner>();

if (telegram.IsEnabled)
{
    builder.Services.AddSingleton<TelegramAuthorization>();
    builder.Services.AddSingleton<IChannelReplySender, TelegramChatReplySender>();
    builder.Services
        .AddHttpClient("telegram_bot_client")
        .AddTypedClient<ITelegramBotClient>((httpClient, sp) =>
        {
            var token = sp.GetRequiredService<IOptions<TelegramOptions>>().Value.BotToken;
            return new TelegramBotClient(token, httpClient);
        });
}

if (web.Enabled)
{
    builder.Services.AddMindBotWeb();
}

// Operator alerts go to the Telegram operator chat when there is one, otherwise into the web
// chat, otherwise to the log.
if (telegram.IsEnabled && telegram.OperatorChatId is not null)
{
    builder.Services.AddSingleton<IOperatorAlertSink, TelegramOperatorAlertSink>();
}
else if (web.Enabled)
{
    builder.Services.AddSingleton<IOperatorAlertSink, WebOperatorAlertSink>();
}
else
{
    builder.Services.AddSingleton<IOperatorAlertSink, LogOperatorAlertSink>();
}

// BaseAddress must end in a slash or HttpClient would discard the last path segment of BASEURL
// when combining it with a relative webhook path.
builder.Services.AddHttpClient<IN8nClient, N8nHttpClient>((serviceProvider, httpClient) =>
{
    var n8n = serviceProvider.GetRequiredService<IOptions<N8nOptions>>().Value;
    if (n8n.IsConfigured)
    {
        httpClient.BaseAddress = new Uri(n8n.BaseUrl.TrimEnd('/') + "/");
    }

    httpClient.Timeout = TimeSpan.FromSeconds(n8n.TimeoutSeconds);
});

builder.Services.AddHttpClient<IVideoTitleResolver, YouTubeOEmbedTitleResolver>(httpClient =>
{
    // Short on purpose: this is a metadata lookup with a fallback, not something worth waiting on.
    httpClient.Timeout = TimeSpan.FromSeconds(15);
});

// Registration order matters. The database must exist before anything reads it, the git self-check
// must pass before work is attempted, and the sync worker must drain the previous run's backlog
// before the poller starts accepting new updates.
builder.Services.AddHostedService<DatabaseStartupHostedService>();
builder.Services.AddHostedService<GitStartupCheckHostedService>();
builder.Services.AddHostedService<VaultSyncHostedService>();
builder.Services.AddHostedService<BackgroundJobHostedService>();
if (telegram.IsEnabled)
{
    builder.Services.AddHostedService<TelegramPollingHostedService>();
}

var app = builder.Build();

app.UseMiddleware<PortGateMiddleware>(webPort);

if (webPort is not null)
{
    app.UseWhen(context => context.Connection.LocalPort == webPort, branch => branch.UseMiddleware<SecurityHeadersMiddleware>());
    app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

    var staticFiles = !string.IsNullOrWhiteSpace(web.StaticRoot) && Directory.Exists(web.StaticRoot)
        ? new PhysicalFileProvider(web.StaticRoot)
        : null;
    if (staticFiles is not null)
    {
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = staticFiles });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = staticFiles });
    }

    app.UseAuthentication();
    app.UseAuthorization();
    app.MapMindBotWebApi();

    if (staticFiles is not null)
    {
        // Client-side routes resolve to the app shell; /api/* is already answered above.
        app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = staticFiles });
    }
}

// A degraded git state (un-pushed commits, dirty tree) is reported but does NOT fail the check: a
// remote that is down for an hour is a condition this bot is designed to ride out, not a broken
// container. Only a stalled poller or an unreachable state database is fatal.
app.MapGet(PortGateMiddleware.HealthPath, async (HealthReportService healthReportService, CancellationToken cancellationToken) =>
{
    var payload = await healthReportService.BuildAsync(cancellationToken);
    var healthy = payload.Status == "healthy";

    return Results.Json(payload, statusCode: healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
});

await app.RunAsync();
return 0;
