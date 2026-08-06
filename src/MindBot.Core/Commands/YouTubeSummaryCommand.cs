using System.Text.Json;
using System.Text.Json.Serialization;
using MindBot.Core.Durability;
using MindBot.Core.Operations;
using MindBot.Core.Options;
using MindBot.Core.YouTube;
using Microsoft.Extensions.Options;

namespace MindBot.Core.Commands;

/// <summary>The JSON stored on the background job. Deserialized by the worker, never by n8n.</summary>
public sealed record YouTubeSummaryPayload(
    [property: JsonPropertyName("videoId")] string VideoId,
    [property: JsonPropertyName("chunkCount")] int? ChunkCount);

/// <summary>
/// Accepts "/ytsummary &lt;youtube-url&gt; [chunks]" and records it as a background job.
/// <para>
/// This command does no network work at all — it validates, then hands off. The n8n pipeline runs
/// in BackgroundJobHostedService because it takes minutes, and ICommand.HandleAsync executes inside
/// the SQLite ingest transaction, which must never span a network round trip.
/// </para>
/// </summary>
public sealed class YouTubeSummaryCommand(IOptions<N8nOptions> n8nOptions) : ICommand
{
    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    private const string Usage = "Usage: /ytsummary <youtube-url> [chunks]";

    public bool Matches(string messageText) =>
        string.Equals(CommandText.ExtractCommand(messageText), "/ytsummary", StringComparison.OrdinalIgnoreCase);

    public Task<CommandResult> HandleAsync(string messageText, IVaultOperationContext context, CancellationToken cancellationToken = default)
    {
        if (!n8nOptions.Value.IsConfigured)
        {
            return Rejected("YouTube summarisation is not configured on this instance (set N8N__BASEURL).");
        }

        var argument = CommandText.ExtractArgument(messageText);
        if (argument.Length == 0)
        {
            return Rejected(Usage);
        }

        var parts = argument.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        var videoId = YouTubeUrl.TryParseVideoId(parts[0]);
        if (videoId is null)
        {
            return Rejected($"'{parts[0]}' is not a YouTube video link. {Usage}");
        }

        int? chunkCount = null;
        if (parts.Length > 1)
        {
            if (!int.TryParse(parts[1], out var requested)
                || requested < ChunkCountCalculator.MinChunks
                || requested > ChunkCountCalculator.MaxChunks)
            {
                return Rejected(
                    $"The chunk count must be a whole number between {ChunkCountCalculator.MinChunks} and {ChunkCountCalculator.MaxChunks}, got '{parts[1]}'.");
            }

            chunkCount = requested;
        }

        var payload = JsonSerializer.Serialize(new YouTubeSummaryPayload(videoId, chunkCount), PayloadJson);

        return Task.FromResult<CommandResult>(new CommandResult.DeferredJob(
            BackgroundJobKinds.YouTubeSummary,
            payload,
            $"Summarising video {videoId}. This takes a few minutes — I'll send the note title and filename when it's done."));
    }

    public static YouTubeSummaryPayload ParsePayload(string payload) =>
        JsonSerializer.Deserialize<YouTubeSummaryPayload>(payload, PayloadJson)
        ?? throw new InvalidOperationException("A youtube-summary job had an empty payload.");

    private static Task<CommandResult> Rejected(string reason) =>
        Task.FromResult<CommandResult>(new CommandResult.Rejected(reason));
}
