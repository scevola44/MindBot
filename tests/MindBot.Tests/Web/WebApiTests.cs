using System.Net;
using System.Net.Http.Json;
using MindBot.Bot.Web;
using MindBot.Core.Durability;
using MindBot.Core.Notes;
using MindBot.Core.Web;
using Microsoft.Extensions.DependencyInjection;

namespace MindBot.Tests.Web;

public sealed class WebApiTests
{
    [Fact]
    public async Task EveryApiRoute_RequiresADeviceToken()
    {
        await using var harness = await WebHarness.StartAsync();
        var anonymous = harness.Client();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/messages")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/commands")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await WebHarness.SendAsync(anonymous, "hello")).StatusCode);
        Assert.Empty(await harness.PendingWriteJobsAsync());
    }

    [Theory]
    [InlineData("mbt_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // well-formed, never issued
    [InlineData("not-a-token")]
    [InlineData("")]
    public async Task AnUnknownToken_IsRejected(string token)
    {
        await using var harness = await WebHarness.StartAsync();
        await harness.AddDeviceAsync();

        var response = await harness.Client(token).GetAsync("/api/session");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ARevokedToken_StopsWorkingImmediately()
    {
        await using var harness = await WebHarness.StartAsync();
        var (_, token) = await harness.AddDeviceAsync("phone");
        var client = harness.Client(token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/session")).StatusCode);

        await harness.Services.GetRequiredService<IDeviceTokenStore>().RevokeAsync("phone");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/session")).StatusCode);
    }

    [Fact]
    public async Task Session_NamesTheDevice()
    {
        await using var harness = await WebHarness.StartAsync();
        var (_, token) = await harness.AddDeviceAsync("laptop");

        var session = await harness.Client(token).GetFromJsonAsync<SessionResponse>("/api/session", WebJson.Options);

        Assert.Equal("laptop", session!.Device);
    }

    [Fact]
    public async Task BareText_QueuesAFleetingNoteOnTheWebChannel_InOneTransaction()
    {
        await using var harness = await WebHarness.StartAsync();
        var (device, token) = await harness.AddDeviceAsync();

        var response = await WebHarness.SendAsync(harness.Client(token), "Just a quick thought", "msg-1");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<SendMessageResponse>(WebJson.Options);

        Assert.False(body!.Duplicate);
        Assert.Equal(WebMessageDirection.In, body.Message.Direction);
        Assert.Equal("msg-1", body.Message.ClientMessageId);
        Assert.Equal(WebMessageDirection.Out, body.Reply.Direction);
        Assert.Equal(body.Message.Id, body.Reply.InReplyToId);
        Assert.EndsWith(".md", body.Reply.Text);

        var job = Assert.Single(await harness.PendingWriteJobsAsync());
        Assert.Equal(WebChat.Address, job.Chat);
        Assert.Equal(device.Id, job.SenderId);
        Assert.Equal(body.Message.Id, job.UpdateId);
        Assert.Equal(VaultLayout.FleetingFolder, job.RelativeFolder);

        await using var check = await harness.Services.GetRequiredService<IIngestUnitOfWorkFactory>().BeginAsync();
        Assert.True(await check.IsUpdateProcessedAsync(Channel.Web, body.Message.Id));
    }

    [Fact]
    public async Task ARetriedMessage_ReturnsTheStoredReply_AndQueuesNothingNew()
    {
        await using var harness = await WebHarness.StartAsync();
        var (_, token) = await harness.AddDeviceAsync();
        var client = harness.Client(token);

        var first = await (await WebHarness.SendAsync(client, "hello", "retry-me")).Content.ReadFromJsonAsync<SendMessageResponse>(WebJson.Options);
        var second = await (await WebHarness.SendAsync(client, "hello", "retry-me")).Content.ReadFromJsonAsync<SendMessageResponse>(WebJson.Options);

        Assert.True(second!.Duplicate);
        Assert.Equal(first!.Message.Id, second.Message.Id);
        Assert.Equal(first.Reply, second.Reply);
        Assert.Single(await harness.PendingWriteJobsAsync());
    }

    [Fact]
    public async Task ConcurrentRetriesOfOneMessage_FileExactlyOneNote()
    {
        await using var harness = await WebHarness.StartAsync();
        var (_, token) = await harness.AddDeviceAsync();

        // A flaky network can deliver the same send several times at once. The ingest transaction
        // holds the write lock from its first statement, so the duplicates queue up behind the
        // first and then find its rows.
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => WebHarness.SendAsync(harness.Client(token), "hello", "same-id")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<SendMessageResponse>(WebJson.Options)));
        Assert.Single(bodies, b => !b!.Duplicate);
        Assert.Single(bodies.Select(b => b!.Reply.Id).Distinct());
        Assert.Single(await harness.PendingWriteJobsAsync());
    }

    [Theory]
    [InlineData(null, "text")]
    [InlineData("", "text")]
    [InlineData("has space", "text")]
    [InlineData("ok-id", "")]
    [InlineData("ok-id", "   ")]
    public async Task InvalidRequests_AreRejected_AndQueueNothing(string? clientMessageId, string text)
    {
        await using var harness = await WebHarness.StartAsync();
        var (_, token) = await harness.AddDeviceAsync();

        var response = await harness.Client(token).PostAsJsonAsync("/api/messages", new { clientMessageId, text });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await harness.PendingWriteJobsAsync());
    }

    [Fact]
    public async Task AnOverlongMessage_IsRejected()
    {
        await using var harness = await WebHarness.StartAsync(web => web.MaxMessageLength = 10);
        var (_, token) = await harness.AddDeviceAsync();

        var response = await WebHarness.SendAsync(harness.Client(token), new string('x', 11));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TheNewFlow_WorksOverTheWebChannel()
    {
        await using var harness = await WebHarness.StartAsync();
        var (_, token) = await harness.AddDeviceAsync();
        var client = harness.Client(token);

        await WebHarness.SendAsync(client, "/new");
        await WebHarness.SendAsync(client, "Groceries");
        var final = await (await WebHarness.SendAsync(client, "Milk and eggs")).Content.ReadFromJsonAsync<SendMessageResponse>(WebJson.Options);

        var job = Assert.Single(await harness.PendingWriteJobsAsync());
        Assert.Equal("groceries.md", job.Filename);
        Assert.Contains("Milk and eggs", job.Content);
        Assert.Equal(job.Filename, final!.Reply.Text);
    }

    [Fact]
    public async Task History_PagesFromACursor()
    {
        await using var harness = await WebHarness.StartAsync();
        var (_, token) = await harness.AddDeviceAsync();
        var client = harness.Client(token);
        await WebHarness.SendAsync(client, "one");
        await WebHarness.SendAsync(client, "two");

        var all = await client.GetFromJsonAsync<MessagesResponse>("/api/messages", WebJson.Options);
        Assert.Equal(["one", "two"], all!.Messages.Where(m => m.Direction == WebMessageDirection.In).Select(m => m.Text));
        Assert.Equal(4, all.Messages.Count);
        Assert.False(all.HasMore);

        var page = await client.GetFromJsonAsync<MessagesResponse>($"/api/messages?after={all.Messages[0].Id}&limit=2", WebJson.Options);
        Assert.Equal(all.Messages.Skip(1).Take(2).Select(m => m.Id), page!.Messages.Select(m => m.Id));
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task Messages_SerializeDirectionAsALowercaseString()
    {
        await using var harness = await WebHarness.StartAsync();
        var (_, token) = await harness.AddDeviceAsync();
        var client = harness.Client(token);
        await WebHarness.SendAsync(client, "hello");

        var json = await client.GetStringAsync("/api/messages");

        Assert.Contains("\"direction\":\"in\"", json);
        Assert.Contains("\"direction\":\"out\"", json);
    }

    [Fact]
    public async Task Commands_ListsTheRouterBuiltIns_AndHidesUnconfiguredOnes()
    {
        await using var harness = await WebHarness.StartAsync();
        var (_, token) = await harness.AddDeviceAsync();

        var names = (await harness.Client(token).GetFromJsonAsync<List<Core.Commands.CommandDescriptor>>("/api/commands", WebJson.Options))!
            .Select(c => c.Name).ToList();

        Assert.Contains("/new", names);
        Assert.Contains("/task", names);
        Assert.Contains("/status", names);
        // N8N__BASEURL is unset in this harness.
        Assert.DoesNotContain("/ytsummary", names);
    }

    [Fact]
    public async Task AnUnknownApiPath_Is404()
    {
        await using var harness = await WebHarness.StartAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await harness.Client().GetAsync("/api/nope")).StatusCode);
    }

    [Fact]
    public async Task ApiResponses_CarryTheSecurityHeaders_AndAreNeverCached()
    {
        await using var harness = await WebHarness.StartAsync(withSecurityHeaders: true);
        var (_, token) = await harness.AddDeviceAsync();

        var response = await harness.Client(token).GetAsync("/api/session");

        Assert.Equal(SecurityHeadersMiddleware.ContentSecurityPolicy, response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task ADeferredReply_LandsInTheWebTranscript()
    {
        await using var harness = await WebHarness.StartAsync();
        var (_, token) = await harness.AddDeviceAsync();

        // What YouTubeSummaryJobRunner does once a web-requested summary is ready.
        await harness.Services.GetRequiredService<Core.Notifications.IChatReplySender>().SendAsync(WebChat.Address, "Summarised \"x\" → x.md");

        var history = await harness.Client(token).GetFromJsonAsync<MessagesResponse>("/api/messages", WebJson.Options);
        var line = Assert.Single(history!.Messages);
        Assert.Equal(WebMessageDirection.Out, line.Direction);
        Assert.Null(line.InReplyToId);
    }

    [Fact]
    public async Task OperatorAlerts_PostIntoTheWebChatAsSystemLines()
    {
        await using var harness = await WebHarness.StartAsync();
        var (_, token) = await harness.AddDeviceAsync();
        var sink = new WebOperatorAlertSink(
            harness.Services.GetRequiredService<IWebTranscript>(),
            harness.Services.GetRequiredService<WebMessageHub>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WebOperatorAlertSink>.Instance);

        await sink.SendAsync("Push failing");

        var history = await harness.Client(token).GetFromJsonAsync<MessagesResponse>("/api/messages", WebJson.Options);
        Assert.Equal(WebMessageDirection.System, Assert.Single(history!.Messages).Direction);
    }
}
