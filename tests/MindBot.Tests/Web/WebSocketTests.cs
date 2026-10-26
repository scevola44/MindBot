using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MindBot.Bot.Web;
using MindBot.Core.Web;

namespace MindBot.Tests.Web;

public sealed class WebSocketTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ABadToken_ClosesTheSocketAsUnauthorized()
    {
        await using var harness = await WebHarness.StartAsync();
        await harness.AddDeviceAsync();

        using var socket = await ConnectAsync(harness);
        await SendAsync(socket, new { type = "auth", token = "mbt_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" });

        var result = await socket.ReceiveAsync(new byte[1024], new CancellationTokenSource(Timeout).Token);

        Assert.Equal(WebSocketMessageType.Close, result.MessageType);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, socket.CloseStatus);
    }

    [Fact]
    public async Task AFirstFrameThatIsNotAuth_ClosesTheSocket()
    {
        await using var harness = await WebHarness.StartAsync();

        using var socket = await ConnectAsync(harness);
        await SendAsync(socket, new { type = "hello" });

        var result = await socket.ReceiveAsync(new byte[1024], new CancellationTokenSource(Timeout).Token);

        Assert.Equal(WebSocketMessageType.Close, result.MessageType);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, socket.CloseStatus);
    }

    [Fact]
    public async Task AfterAuth_SendsTheBacklog_ThenLiveMessages()
    {
        await using var harness = await WebHarness.StartAsync();
        var (_, token) = await harness.AddDeviceAsync("phone");
        var client = harness.Client(token);
        await WebHarness.SendAsync(client, "before connecting");

        using var socket = await ConnectAsync(harness);
        await SendAsync(socket, new { type = "auth", token, after = 0 });

        var ready = await ReceiveAsync(socket);
        Assert.Equal("ready", ready.GetProperty("type").GetString());
        Assert.Equal("phone", ready.GetProperty("device").GetString());

        // Backlog: the earlier message and its reply.
        Assert.Equal("before connecting", (await ReceiveMessageAsync(socket)).Text);
        Assert.Equal(WebMessageDirection.Out, (await ReceiveMessageAsync(socket)).Direction);

        // Live: another device sends; this one sees both lines.
        await WebHarness.SendAsync(client, "while connected");
        Assert.Equal("while connected", (await ReceiveMessageAsync(socket)).Text);
        Assert.Equal(WebMessageDirection.Out, (await ReceiveMessageAsync(socket)).Direction);
    }

    [Fact]
    public async Task ACursor_SkipsWhatTheClientAlreadyHas()
    {
        await using var harness = await WebHarness.StartAsync();
        var (_, token) = await harness.AddDeviceAsync();
        var client = harness.Client(token);
        var sent = await (await WebHarness.SendAsync(client, "already seen")).Content.ReadFromJsonAsync<SendMessageResponse>(WebJson.Options);

        using var socket = await ConnectAsync(harness);
        await SendAsync(socket, new { type = "auth", token, after = sent!.Reply.Id });
        Assert.Equal("ready", (await ReceiveAsync(socket)).GetProperty("type").GetString());

        await WebHarness.SendAsync(client, "new");

        // The first thing after "ready" is the new message, not the backlog.
        Assert.Equal("new", (await ReceiveMessageAsync(socket)).Text);
    }

    [Fact]
    public async Task ADeferredReply_IsPushedToConnectedDevices()
    {
        await using var harness = await WebHarness.StartAsync();
        var (_, token) = await harness.AddDeviceAsync();

        using var socket = await ConnectAsync(harness);
        await SendAsync(socket, new { type = "auth", token, after = 0 });
        await ReceiveAsync(socket); // ready

        await ((Core.Notifications.IChatReplySender)harness.Services.GetService(typeof(Core.Notifications.IChatReplySender))!)
            .SendAsync(WebChat.Address, "Summarised \"x\" → x.md");

        var pushed = await ReceiveMessageAsync(socket);
        Assert.Equal(WebMessageDirection.Out, pushed.Direction);
        Assert.StartsWith("Summarised", pushed.Text);
    }

    [Fact]
    public async Task UnderConcurrentSends_ADeviceGetsEveryLineInOrderWithNoGaps()
    {
        await using var harness = await WebHarness.StartAsync();
        var (_, token) = await harness.AddDeviceAsync();

        using var socket = await ConnectAsync(harness);
        await SendAsync(socket, new { type = "auth", token, after = 0 });
        await ReceiveAsync(socket); // ready

        // Commits happen in id order, but their announcements can race. The stream must not care.
        const int Sends = 20;
        await Task.WhenAll(Enumerable.Range(0, Sends).Select(i => WebHarness.SendAsync(harness.Client(token), $"message {i}")));

        var ids = new List<long>();
        for (var i = 0; i < Sends * 2; i++)
        {
            ids.Add((await ReceiveMessageAsync(socket)).Id);
        }

        Assert.Equal(Enumerable.Range(1, Sends * 2).Select(i => (long)i), ids);
    }

    [Fact]
    public async Task RevokingADevice_ClosesItsOpenSocket()
    {
        await using var harness = await WebHarness.StartAsync();
        var (_, token) = await harness.AddDeviceAsync("phone");
        var (_, otherToken) = await harness.AddDeviceAsync("laptop");

        using var socket = await ConnectAsync(harness);
        await SendAsync(socket, new { type = "auth", token, after = 0 });
        await ReceiveAsync(socket); // ready

        await ((IDeviceTokenStore)harness.Services.GetService(typeof(IDeviceTokenStore))!).RevokeAsync("phone");

        // The next line written to the chat must not reach the revoked device.
        await WebHarness.SendAsync(harness.Client(otherToken), "after revocation");

        var result = await socket.ReceiveAsync(new byte[64 * 1024], new CancellationTokenSource(Timeout).Token);
        Assert.Equal(WebSocketMessageType.Close, result.MessageType);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, socket.CloseStatus);
    }

    private static async Task<WebSocket> ConnectAsync(WebHarness harness)
    {
        var client = harness.Server.CreateWebSocketClient();
        return await client.ConnectAsync(new Uri("ws://localhost/api/ws"), new CancellationTokenSource(Timeout).Token);
    }

    private static Task SendAsync<T>(WebSocket socket, T payload) =>
        socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(payload, WebJson.Options), WebSocketMessageType.Text, true, CancellationToken.None);

    private static async Task<JsonElement> ReceiveAsync(WebSocket socket)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var buffer = new byte[64 * 1024];
        var count = 0;
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(count), timeout.Token);
            Assert.NotEqual(WebSocketMessageType.Close, result.MessageType);
            count += result.Count;
            if (result.EndOfMessage)
            {
                return JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, count)).RootElement.Clone();
            }
        }
    }

    private static async Task<WebMessageDto> ReceiveMessageAsync(WebSocket socket)
    {
        var frame = await ReceiveAsync(socket);
        Assert.Equal("message", frame.GetProperty("type").GetString());
        return frame.GetProperty("message").Deserialize<WebMessageDto>(WebJson.Options)!;
    }
}
