using Microsoft.Extensions.Logging;

namespace MindBot.Core.YouTube;

/// <summary>What a completed pipeline run produced, ready to become a note.</summary>
public sealed record YouTubeSummaryResult(
    string VideoId,
    string VideoUrl,
    string Title,
    string Summary,
    IReadOnlyList<string> Keywords);

/// <summary>
/// Runs the five n8n webhooks in order and pairs the result with the video's title. Pure
/// orchestration — no HTTP, no filesystem, no database — so the ordering and the chunk-count
/// decision are testable against a fake client.
/// <para>
/// This deliberately does <em>not</em> run inside the ingest transaction. Five sequential webhooks,
/// several of them LLM-bound, take minutes; holding a SQLite write lock across them would stall
/// every other capture.
/// </para>
/// </summary>
public sealed class YouTubeSummaryPipeline(
    IN8nClient client,
    IVideoTitleResolver titleResolver,
    ILogger<YouTubeSummaryPipeline> logger)
{
    /// <param name="requestedChunkCount">
    /// The user's explicit chunk count, or null to derive one from the transcript's length.
    /// </param>
    public async Task<YouTubeSummaryResult> RunAsync(
        string videoId,
        int? requestedChunkCount,
        CancellationToken cancellationToken = default)
    {
        var canonicalUrl = YouTubeUrl.CanonicalUrl(videoId);

        var transcript = await client.GetTranscriptAsync(new TranscriptRequest(canonicalUrl), cancellationToken);
        if (transcript.Segments.Count == 0)
        {
            throw new N8nException($"get-yt-transcript returned no segments for {videoId}; the video may have no captions.");
        }

        var chunkCount = requestedChunkCount ?? ChunkCountCalculator.ForSegments(transcript.Segments);
        logger.LogInformation(
            "Summarising {VideoId}: {Segments} segment(s) into {Chunks} chunk(s){Origin}.",
            videoId,
            transcript.Segments.Count,
            chunkCount,
            requestedChunkCount is null ? " (derived)" : " (requested)");

        var chunked = await client.ChunkTextAsync(new ChunkerRequest(transcript.Segments, chunkCount), cancellationToken);
        var summarised = await client.SummarizeChunksAsync(chunked, cancellationToken);
        var reduced = await client.ReduceChunksAsync(summarised, cancellationToken);
        var keywords = await client.ExtractKeywordsAsync(reduced, cancellationToken);

        // Last, and non-fatal: by this point the expensive work is done and must not be thrown away.
        var title = await titleResolver.ResolveTitleAsync(canonicalUrl, cancellationToken);

        return new YouTubeSummaryResult(
            transcript.VideoId,
            canonicalUrl,
            string.IsNullOrWhiteSpace(title) ? videoId : title.Trim(),
            reduced.Summary,
            keywords.Keywords);
    }
}
