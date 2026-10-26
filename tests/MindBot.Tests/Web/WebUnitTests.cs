using MindBot.Bot.Web;
using MindBot.Core.Commands;
using MindBot.Core.Notifications;
using MindBot.Core.Options;
using MindBot.Core.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MindBot.Tests.Web;

public sealed class PortGateTests
{
    private const int WebPort = 8081;

    [Theory]
    [InlineData(WebOptions.HealthPort, "/health", true)]
    [InlineData(WebOptions.HealthPort, "/api/messages", false)]
    [InlineData(WebOptions.HealthPort, "/", false)]
    [InlineData(WebPort, "/api/messages", true)]
    [InlineData(WebPort, "/", true)]
    [InlineData(WebPort, "/health", false)]
    [InlineData(WebPort, "/HEALTH", false)]
    [InlineData(9999, "/api/messages", false)]
    public void ARequestIsServedOnlyOnItsOwnPort(int localPort, string path, bool allowed) =>
        Assert.Equal(allowed, PortGateMiddleware.IsAllowed(localPort, new PathString(path), WebPort));

    [Fact]
    public void WithTheWebChannelOff_OnlyHealthIsServed()
    {
        Assert.True(PortGateMiddleware.IsAllowed(WebOptions.HealthPort, "/health", webPort: null));
        Assert.False(PortGateMiddleware.IsAllowed(WebPort, "/api/messages", webPort: null));
    }

    [Fact]
    public async Task TheHostHeaderCannotReachHealthThroughTheWebPort()
    {
        var context = new DefaultHttpContext();
        context.Connection.LocalPort = WebPort;
        context.Request.Path = "/health";
        context.Request.Host = new HostString("localhost", WebOptions.HealthPort);
        var reachedNext = false;

        await new PortGateMiddleware(_ => { reachedNext = true; return Task.CompletedTask; }, WebPort).InvokeAsync(context);

        Assert.False(reachedNext);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }
}

public sealed class DeviceTokensTests
{
    [Fact]
    public void GeneratedTokens_AreWellFormedAndUnique()
    {
        var tokens = Enumerable.Range(0, 100).Select(_ => DeviceTokens.Generate()).ToList();

        Assert.All(tokens, t => Assert.True(DeviceTokens.LooksValid(t), t));
        Assert.Equal(100, tokens.Distinct().Count());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("mbt_short")]
    [InlineData("xyz_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("mbt_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA!A")]
    public void MalformedTokens_AreRejectedBeforeAnyLookup(string? token) => Assert.False(DeviceTokens.LooksValid(token));

    [Fact]
    public void TheHash_IsStableAndNotTheToken()
    {
        var token = DeviceTokens.Generate();

        Assert.Equal(DeviceTokens.Hash(token), DeviceTokens.Hash(token));
        Assert.Equal(64, DeviceTokens.Hash(token).Length);
        Assert.DoesNotContain(token[4..], DeviceTokens.Hash(token));
    }
}

public sealed class WebMessageHubTests
{
    [Fact]
    public async Task ASubscriberIsRungWhenTheTranscriptGrows()
    {
        var hub = new WebMessageHub();
        using var subscription = hub.Subscribe();

        hub.NotifyNewMessages();

        Assert.True(await subscription.Rings.ReadAsync());
    }

    [Fact]
    public void ABurstCoalescesIntoOneRing_SoNothingBuffersWithoutBound()
    {
        var hub = new WebMessageHub();
        using var subscription = hub.Subscribe();

        for (var i = 0; i < 1000; i++)
        {
            hub.NotifyNewMessages();
        }

        Assert.True(subscription.Rings.TryRead(out _));
        Assert.False(subscription.Rings.TryRead(out _));
        Assert.Equal(1, hub.SubscriberCount);
    }

    [Fact]
    public void DisposingASubscription_Unsubscribes()
    {
        var hub = new WebMessageHub();
        hub.Subscribe().Dispose();

        Assert.Equal(0, hub.SubscriberCount);
    }
}

public sealed class LatchingOperatorNotifierTests
{
    private sealed class RecordingSink : IOperatorAlertSink
    {
        public List<string> Sent { get; } = [];

        public Task SendAsync(string message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task AnOngoingCondition_AlertsOnceUntilCleared()
    {
        var sink = new RecordingSink();
        var notifier = new LatchingOperatorNotifier(sink, NullLogger<LatchingOperatorNotifier>.Instance);

        await notifier.NotifyOnceAsync(OperatorAlertKeys.PushFailing, "push failing");
        await notifier.NotifyOnceAsync(OperatorAlertKeys.PushFailing, "push failing");
        await notifier.ClearAsync(OperatorAlertKeys.PushFailing);
        await notifier.NotifyOnceAsync(OperatorAlertKeys.PushFailing, "push failing again");
        await notifier.NotifyAsync("branch rewritten");

        Assert.Equal(["push failing", "push failing again", "branch rewritten"], sink.Sent);
    }
}

public sealed class CommandCatalogTests
{
    [Fact]
    public void EveryRegisteredCommand_IsInTheCatalog()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new VaultOptions { Root = "/tmp" }));
        services.AddSingleton(Options.Create(new N8nOptions()));
        services.AddSingleton<Core.Health.HealthSnapshot>();
        services.AddSingleton<Core.Durability.IWriteJobQueue, Fakes.InMemoryWriteJobQueue>();
        services.AddMindBotCommands();
        var commands = services.BuildServiceProvider().GetServices<ICommand>().Where(c => c is not BareTextCommand).ToList();

        // A command no catalog entry matches would be invisible in the clients' autocomplete.
        Assert.All(commands, command =>
            Assert.Contains(CommandCatalog.All, entry => command.Matches(entry.Name)));
    }

    [Fact]
    public void CatalogNames_AreUniqueSlashCommands()
    {
        Assert.All(CommandCatalog.All, c => Assert.StartsWith("/", c.Name));
        Assert.Equal(CommandCatalog.All.Count, CommandCatalog.All.Select(c => c.Name).Distinct().Count());
    }
}
