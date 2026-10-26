using System.Text.Json;
using System.Text.Json.Serialization;
using MindBot.Core.Commands;
using MindBot.Core.Options;
using MindBot.Core.Web;
using Microsoft.Extensions.Options;

namespace MindBot.Bot.Web;

/// <summary>One JSON shape for HTTP and WebSocket alike: camelCase names, enums as camelCase strings.</summary>
public static class WebJson
{
    public static JsonSerializerOptions Options { get; } = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

public sealed record SendMessageRequest(string? ClientMessageId, string? Text);

public sealed record WebMessageDto(
    long Id,
    string? ClientMessageId,
    WebMessageDirection Direction,
    string Text,
    DateTimeOffset CreatedAt,
    long? InReplyToId)
{
    public static WebMessageDto From(WebMessage message) =>
        new(message.Id, message.ClientMessageId, message.Direction, message.Text, message.CreatedAt, message.InReplyToId);
}

public sealed record SendMessageResponse(WebMessageDto Message, WebMessageDto Reply, bool Duplicate);

public sealed record MessagesResponse(IReadOnlyList<WebMessageDto> Messages, bool HasMore);

public sealed record SessionResponse(string Device);

/// <summary>
/// The chat API. Every route except the WebSocket requires a device token; the WebSocket
/// authenticates in its first frame instead (see <see cref="WebSocketSession"/>).
/// </summary>
public static class WebApi
{
    public const int MaxClientMessageIdLength = 100;

    private const int MaxPageSize = 500;

    public static IEndpointRouteBuilder MapMindBotWebApi(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api").RequireAuthorization();

        // Lets the setup screen check a pasted token before saving it.
        api.MapGet("/session", (HttpContext context) =>
            Results.Ok(new SessionResponse(context.User.Identity?.Name ?? string.Empty)));

        api.MapGet("/commands", (IOptions<N8nOptions> n8n) =>
            Results.Ok(CommandCatalog.All.Where(c => c.Name != CommandCatalog.YouTubeSummary || n8n.Value.IsConfigured)));

        api.MapGet("/messages", async (IWebTranscript transcript, long? after, int? limit, CancellationToken cancellationToken) =>
        {
            var pageSize = Math.Clamp(limit ?? MaxPageSize, 1, MaxPageSize);
            var page = await transcript.GetAfterAsync(after ?? 0, pageSize + 1, cancellationToken);
            return Results.Ok(new MessagesResponse(page.Take(pageSize).Select(WebMessageDto.From).ToList(), page.Count > pageSize));
        });

        api.MapPost("/messages", async (
            SendMessageRequest request,
            HttpContext context,
            WebMessageService service,
            IOptions<WebOptions> webOptions,
            CancellationToken cancellationToken) =>
        {
            if (!IsValidClientMessageId(request.ClientMessageId))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["clientMessageId"] = [$"Required: 1-{MaxClientMessageIdLength} characters of A-Z, a-z, 0-9, '-' or '_'. Generate one per message and reuse it on retry."],
                });
            }

            if (string.IsNullOrWhiteSpace(request.Text))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["text"] = ["A message cannot be empty."] });
            }

            if (request.Text.Length > webOptions.Value.MaxMessageLength)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["text"] = [$"A message can be at most {webOptions.Value.MaxMessageLength} characters."],
                });
            }

            var result = await service.AcceptAsync(
                DeviceTokenAuthenticationHandler.DeviceId(context.User),
                request.ClientMessageId!,
                request.Text,
                cancellationToken);

            return Results.Ok(new SendMessageResponse(WebMessageDto.From(result.Inbound), WebMessageDto.From(result.Reply), result.WasDuplicate));
        });

        endpoints.Map("/api/ws", (HttpContext context, WebSocketSession session) => session.RunAsync(context)).AllowAnonymous();

        // Anything else under /api is a 404, never the web client's index.html.
        endpoints.Map("/api/{**rest}", () => Results.NotFound()).AllowAnonymous();

        return endpoints;
    }

    public static bool IsValidClientMessageId(string? id) =>
        !string.IsNullOrEmpty(id)
        && id.Length <= MaxClientMessageIdLength
        && id.AsSpan().IndexOfAnyExcept("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_") < 0;
}
