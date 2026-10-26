using MindBot.Bot.Web;
using MindBot.Core.Web;
using MindBot.Infrastructure.State;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MindBot.Tests.Web;

public sealed class DeviceStoreTests
{
    [Fact]
    public async Task OnlyTheHash_IsStored()
    {
        await using var harness = await WebHarness.StartAsync();
        var (_, token) = await harness.AddDeviceAsync();

        await using var db = await harness.Services.GetRequiredService<IDbContextFactory<MindBotDbContext>>().CreateDbContextAsync();
        var row = await db.DeviceTokens.SingleAsync();

        Assert.Equal(DeviceTokens.Hash(token), row.TokenHash);
        Assert.DoesNotContain(token, File.ReadAllText(harness.DatabasePath, System.Text.Encoding.Latin1));
    }

    [Fact]
    public async Task TwoActiveDevices_CannotShareAName_ButARevokedNameCanBeReused()
    {
        await using var harness = await WebHarness.StartAsync();
        var store = harness.Services.GetRequiredService<IDeviceTokenStore>();
        await store.CreateAsync("phone");

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateAsync("phone"));

        Assert.True(await store.RevokeAsync("phone"));
        var (_, newToken) = await store.CreateAsync("phone");

        Assert.NotNull(await store.AuthenticateAsync(newToken));
        Assert.Equal(2, (await store.ListAsync()).Count);
    }

    [Fact]
    public async Task Authenticating_RecordsLastSeen()
    {
        await using var harness = await WebHarness.StartAsync();
        var store = harness.Services.GetRequiredService<IDeviceTokenStore>();
        var (_, token) = await store.CreateAsync("phone");

        await store.AuthenticateAsync(token);

        Assert.Equal(harness.Time.GetUtcNow(), Assert.Single(await store.ListAsync()).LastSeenAt);
    }

    [Fact]
    public async Task TheCli_AddsListsAndRevokesDevices()
    {
        await using var harness = await WebHarness.StartAsync();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["STATE:DATABASEPATH"] = harness.DatabasePath })
            .Build();

        var (code, output, _) = await RunCliAsync(config, "add", "tablet");
        Assert.Equal(0, code);
        var token = output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Single(DeviceTokens.LooksValid);
        Assert.NotNull(await harness.Services.GetRequiredService<IDeviceTokenStore>().AuthenticateAsync(token));

        (code, output, _) = await RunCliAsync(config, "list");
        Assert.Equal(0, code);
        Assert.Contains("tablet", output);
        Assert.DoesNotContain(token, output);

        (code, _, _) = await RunCliAsync(config, "revoke", "tablet");
        Assert.Equal(0, code);
        Assert.Null(await harness.Services.GetRequiredService<IDeviceTokenStore>().AuthenticateAsync(token));

        (code, _, var error) = await RunCliAsync(config, "revoke", "tablet");
        Assert.Equal(1, code);
        Assert.Contains("No active device", error);

        (code, _, error) = await RunCliAsync(config, "frobnicate");
        Assert.Equal(2, code);
        Assert.Contains("Usage", error);
    }

    [Fact]
    public async Task TheCli_RefusesARelativeDatabasePath()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["STATE:DATABASEPATH"] = "mindbot.db" })
            .Build();

        var (code, _, error) = await RunCliAsync(config, "list");

        Assert.Equal(2, code);
        Assert.Contains("STATE__DATABASEPATH", error);
    }

    private static async Task<(int Code, string Output, string Error)> RunCliAsync(IConfiguration config, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = await DeviceCli.RunAsync(args, output, error, config);
        return (code, output.ToString(), error.ToString());
    }
}

public sealed class WebTranscriptPruningTests
{
    [Fact]
    public async Task LinesPastTheRetentionWindow_ArePruned_AndIdsAreNeverReused()
    {
        await using var harness = await WebHarness.StartAsync(web => web.HistoryRetentionDays = 30);
        var transcript = harness.Services.GetRequiredService<IWebTranscript>();
        var old = await transcript.AppendAsync(WebMessageDirection.Out, "old");

        harness.Time.Now = harness.Time.Now.AddDays(31);
        var recent = await transcript.AppendAsync(WebMessageDirection.Out, "recent");
        await harness.Services.GetRequiredService<StateMaintenance>().PruneAsync();

        Assert.Equal(["recent"], (await transcript.GetAfterAsync(0, 10)).Select(m => m.Text));

        // AUTOINCREMENT: a client whose cursor is `recent.Id` must never be handed a reused id.
        var next = await transcript.AppendAsync(WebMessageDirection.Out, "next");
        Assert.True(next.Id > recent.Id && recent.Id > old.Id);
    }
}
