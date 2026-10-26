using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MindBot.Core.Web;

namespace MindBot.Bot.Web;

/// <summary>
/// One live connection. The protocol is deliberately small, because sending goes through
/// POST /api/messages (which is idempotent) and the socket only ever pushes.
/// <code>
/// client → {"type":"auth","token":"mbt_...","after":123}   first frame, within AuthTimeout
/// server → {"type":"ready","device":"phone"}
/// server → {"type":"message","message":{...}}             every line after "after", in id order
/// </code>
/// The token travels in the first frame, never in the URL: query strings end up in proxy and
/// access logs. Every line sent is read from the transcript after the last id this session sent,
/// so the stream has no gaps and no repeats, and a client's highest id is always a safe resume
/// cursor. The session subscribes to the hub <em>before</em> its first read, so a line committed
/// in between rings the bell instead of being missed.
/// </summary>
public sealed class WebSocketSession(
    IDeviceTokenStore deviceTokens,
    IWebTranscript transcript,
    WebMessageHub hub,
    ILogger<WebSocketSession> logger)
{
    public static readonly TimeSpan AuthTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How often an idle session re-checks its token. Revoking a device must cut off a socket
    /// that is already open, not only new requests; a session also re-checks before every send.
    /// </summary>
    public static readonly TimeSpan RecheckInterval = TimeSpan.FromSeconds(60);

    private const int MaxClientFrameBytes = 4096;
    private const int PageSize = 200;

    private sealed record ClientFrame(string? Type, string? Token, long? After);

    public async Task RunAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var aborted = context.RequestAborted;

        var auth = await ReceiveAuthAsync(socket, aborted);
        var device = auth is null ? null : await deviceTokens.AuthenticateAsync(auth.Token, aborted);
        if (auth is null || device is null)
        {
            await CloseAsync(socket, WebSocketCloseStatus.PolicyViolation, "unauthorized");
            return;
        }

        using var subscription = hub.Subscribe();
        using var sessionEnd = CancellationTokenSource.CreateLinkedTokenSource(aborted);

        try
        {
            await SendAsync(socket, new { type = "ready", device = device.Name }, sessionEnd.Token);

            // Watches for the client closing (or sending anything after auth, which it must not).
            var receiveLoop = DrainClientAsync(socket, sessionEnd);

            var lastSentId = await SendNewAsync(socket, auth.After ?? 0, sessionEnd.Token);

            while (await WaitForRingAsync(subscription, sessionEnd.Token))
            {
                if (await deviceTokens.AuthenticateAsync(auth.Token, sessionEnd.Token) is null)
                {
                    logger.LogInformation("Closing the web chat socket of device {DeviceId}: its token was revoked.", device.Id);
                    await CloseAsync(socket, WebSocketCloseStatus.PolicyViolation, "unauthorized");
                    return;
                }

                lastSentId = await SendNewAsync(socket, lastSentId, sessionEnd.Token);
            }

            await receiveLoop;
        }
        catch (OperationCanceledException)
        {
            // Client went away or the host is stopping.
        }
        catch (WebSocketException ex)
        {
            logger.LogDebug(ex, "Web chat socket for device {DeviceId} ended abruptly.", device.Id);
        }
        finally
        {
            await CloseAsync(socket, WebSocketCloseStatus.NormalClosure, "bye");
        }
    }

    /// <summary>
    /// True when the bell rang or <see cref="RecheckInterval"/> passed (both lead to a token
    /// re-check and a read), false when the subscription ended.
    /// </summary>
    private static async Task<bool> WaitForRingAsync(WebMessageHub.Subscription subscription, CancellationToken cancellationToken)
    {
        using var interval = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        interval.CancelAfter(RecheckInterval);

        try
        {
            if (!await subscription.Rings.WaitToReadAsync(interval.Token))
            {
                return false;
            }

            subscription.Rings.TryRead(out _);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return true;
        }
    }

    /// <summary>Sends every stored line after <paramref name="afterId"/> and returns the last id sent.</summary>
    private async Task<long> SendNewAsync(WebSocket socket, long afterId, CancellationToken cancellationToken)
    {
        var lastSentId = afterId;
        while (true)
        {
            var page = await transcript.GetAfterAsync(lastSentId, PageSize, cancellationToken);
            foreach (var message in page)
            {
                await SendMessageAsync(socket, message, cancellationToken);
                lastSentId = message.Id;
            }

            if (page.Count < PageSize)
            {
                return lastSentId;
            }
        }
    }

    private static async Task<ClientFrame?> ReceiveAuthAsync(WebSocket socket, CancellationToken aborted)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(aborted);
        timeout.CancelAfter(AuthTimeout);

        try
        {
            var text = await ReceiveTextAsync(socket, timeout.Token);
            var frame = text is null ? null : JsonSerializer.Deserialize<ClientFrame>(text, WebJson.Options);
            return frame is { Type: "auth" } ? frame : null;
        }
        catch (Exception ex) when (ex is OperationCanceledException or JsonException or WebSocketException)
        {
            return null;
        }
    }

    private static async Task DrainClientAsync(WebSocket socket, CancellationTokenSource sessionEnd)
    {
        try
        {
            while (await ReceiveTextAsync(socket, sessionEnd.Token) is not null)
            {
                // Nothing is expected after auth; ignore it rather than guess at intent.
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
        }
        finally
        {
            await sessionEnd.CancelAsync();
        }
    }

    /// <summary>One whole text message, or null when the client closed. Oversized frames end the session.</summary>
    private static async Task<string?> ReceiveTextAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxClientFrameBytes];
        var count = 0;
        while (true)
        {
            if (count == buffer.Length)
            {
                throw new WebSocketException(WebSocketError.Faulted, "Client frame too large.");
            }

            var result = await socket.ReceiveAsync(buffer.AsMemory(count), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            count += result.Count;
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(buffer, 0, count);
            }
        }
    }

    private static Task SendMessageAsync(WebSocket socket, WebMessage message, CancellationToken cancellationToken) =>
        SendAsync(socket, new { type = "message", message = WebMessageDto.From(message) }, cancellationToken);

    private static Task SendAsync<T>(WebSocket socket, T payload, CancellationToken cancellationToken) =>
        socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(payload, WebJson.Options), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);

    private static async Task CloseAsync(WebSocket socket, WebSocketCloseStatus status, string description)
    {
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await socket.CloseOutputAsync(status, description, timeout.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
        }
    }
}
