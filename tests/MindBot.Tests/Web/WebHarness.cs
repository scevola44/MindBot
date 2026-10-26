using System.Net.Http.Headers;
using System.Net.Http.Json;
using MindBot.Bot.Services;
using MindBot.Bot.Web;
using MindBot.Core.Commands;
using MindBot.Core.Durability;
using MindBot.Core.Health;
using MindBot.Core.Ingest;
using MindBot.Core.Notes;
using MindBot.Core.Notifications;
using MindBot.Core.Options;
using MindBot.Core.Web;
using MindBot.Infrastructure.State;
using MindBot.Tests.Fakes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MindBot.Tests.Web;

/// <summary>
/// The real web API, router, commands and SQLite store, hosted in-process. Only the Telegram and
/// git sides are absent: these tests are about accepting and answering web messages.
/// </summary>
public sealed class WebHarness : IAsyncDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mindbot-web-tests-" + Guid.NewGuid());

    private WebHarness()
    {
    }

    public WebApplication App { get; private set; } = null!;

    public TestServer Server { get; private set; } = null!;

    public FixedTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero));

    public IServiceProvider Services => App.Services;

    public string DatabasePath => Path.Combine(_root, "data", "mindbot.db");

    public static async Task<WebHarness> StartAsync(Action<WebOptions>? configureWeb = null, bool withSecurityHeaders = false)
    {
        var harness = new WebHarness();
        await harness.InitializeAsync(configureWeb, withSecurityHeaders);
        return harness;
    }

    private async Task InitializeAsync(Action<WebOptions>? configureWeb, bool withSecurityHeaders)
    {
        var vaultRoot = Path.Combine(_root, "vault");
        Directory.CreateDirectory(Path.Combine(vaultRoot, VaultLayout.FleetingFolder));
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);

        var webOptions = new WebOptions { Enabled = true };
        configureWeb?.Invoke(webOptions);

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();

        var services = builder.Services;
        services.AddSingleton<TimeProvider>(Time);
        services.AddSingleton(Options.Create(new VaultOptions { Root = vaultRoot }));
        services.AddSingleton(Options.Create(new StateOptions { DatabasePath = DatabasePath }));
        services.AddSingleton(Options.Create(webOptions));
        services.AddSingleton(Options.Create(new N8nOptions()));
        services.AddSingleton(Options.Create(new TelegramOptions()));
        services.AddMindBotState();
        services.AddMindBotCommands();
        services.AddSingleton<HealthSnapshot>();
        services.AddSingleton<NotePlanner>();
        services.AddSingleton<MessageRouter>();
        services.AddSingleton<WriteJobSignal>();
        services.AddSingleton<BackgroundJobSignal>();
        services.AddSingleton<IChatReplySender, RoutingChatReplySender>();
        services.AddMindBotWeb();

        App = builder.Build();
        if (withSecurityHeaders)
        {
            App.UseMiddleware<SecurityHeadersMiddleware>();
        }

        App.UseWebSockets();
        App.UseAuthentication();
        App.UseAuthorization();
        App.MapMindBotWebApi();

        await using (var db = await App.Services.GetRequiredService<IDbContextFactory<MindBotDbContext>>().CreateDbContextAsync())
        {
            await db.Database.MigrateAsync();
        }

        await App.StartAsync();
        Server = App.GetTestServer();
    }

    public async Task<(DeviceInfo Device, string Token)> AddDeviceAsync(string name = "phone") =>
        await Services.GetRequiredService<IDeviceTokenStore>().CreateAsync(name);

    public HttpClient Client(string? token = null)
    {
        var client = Server.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    public static Task<HttpResponseMessage> SendAsync(HttpClient client, string text, string? clientMessageId = null) =>
        client.PostAsJsonAsync("/api/messages", new { clientMessageId = clientMessageId ?? Guid.NewGuid().ToString("N"), text });

    public async Task<IReadOnlyList<WriteJob>> PendingWriteJobsAsync() =>
        await Services.GetRequiredService<IWriteJobQueue>().GetPendingAsync(100);

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
