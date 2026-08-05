namespace MindBot.Core.YouTube;

/// <summary>
/// The five n8n webhooks, one method each. Kept as a Core abstraction so
/// <see cref="YouTubeSummaryPipeline"/> — which owns the ordering and the chunk-count decision —
/// can be tested without HTTP.
/// </summary>
public interface IN8nClient
{
    Task<TranscriptResult> GetTranscriptAsync(TranscriptRequest request, CancellationToken cancellationToken = default);

    Task<ChunkerResult> ChunkTextAsync(ChunkerRequest request, CancellationToken cancellationToken = default);

    Task<SummarizeResult> SummarizeChunksAsync(ChunkerResult request, CancellationToken cancellationToken = default);

    Task<ReduceResult> ReduceChunksAsync(SummarizeResult request, CancellationToken cancellationToken = default);

    Task<KeywordsResult> ExtractKeywordsAsync(ReduceResult request, CancellationToken cancellationToken = default);
}

/// <summary>Raised when a webhook is unreachable, returns a non-success status, or returns a body this client cannot read.</summary>
public sealed class N8nException(string message, Exception? innerException = null) : Exception(message, innerException);
