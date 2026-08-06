using System.Net;
using MindBot.Core.YouTube;

namespace MindBot.Tests.Fakes;

/// <summary>
/// Records the pipeline's calls in order and hands back canned responses, so ordering and the
/// derived chunk count can be asserted without HTTP.
/// </summary>
public sealed class FakeN8nClient : IN8nClient
{
    public List<string> Calls { get; } = [];

    public ChunkerRequest? ChunkerRequest { get; private set; }

    public ChunkerResult? SummarizeRequest { get; private set; }

    public SummarizeResult? ReduceRequest { get; private set; }

    public ReduceResult? KeywordsRequest { get; private set; }

    public TranscriptResult Transcript { get; set; } = new(
        "full transcript",
        [new TranscriptSegment("one two three", 0, 3)],
        "qIeJ7Gw9v_I",
        "https://youtube.com/watch?v=qIeJ7Gw9v_I");

    public string Summary { get; set; } = "## Summary\nIt was about tabletop games.";

    public IReadOnlyList<string> Keywords { get; set; } = ["Daggerheart"];

    /// <summary>Set to make the named call throw, exercising the runner's failure handling.</summary>
    public string? FailOnCall { get; set; }

    /// <summary>The status the failing call's exception carries. Defaults to a plain 500.</summary>
    public HttpStatusCode FailureStatusCode { get; set; } = HttpStatusCode.InternalServerError;

    public Task<TranscriptResult> GetTranscriptAsync(TranscriptRequest request, CancellationToken cancellationToken = default)
    {
        Record("get-yt-transcript", cancellationToken);
        return Task.FromResult(Transcript);
    }

    public Task<ChunkerResult> ChunkTextAsync(ChunkerRequest request, CancellationToken cancellationToken = default)
    {
        Record("text-chunker", cancellationToken);
        ChunkerRequest = request;

        var chunks = Enumerable.Range(0, request.ChunksNumber)
            .Select(i => new TextChunk(i, $"chunk {i}", i * 100, (i + 1) * 100, 10))
            .ToList();

        return Task.FromResult(new ChunkerResult(request.ChunksNumber, chunks));
    }

    public Task<SummarizeResult> SummarizeChunksAsync(ChunkerResult request, CancellationToken cancellationToken = default)
    {
        Record("summarize-chunks", cancellationToken);
        SummarizeRequest = request;

        var summaries = request.Chunks
            .Select(c => new ChunkSummary(c.ChunkIndex, "0:00", "1:40", $"summary of chunk {c.ChunkIndex}"))
            .ToList();

        return Task.FromResult(new SummarizeResult(request.NumChunks, summaries));
    }

    public Task<ReduceResult> ReduceChunksAsync(SummarizeResult request, CancellationToken cancellationToken = default)
    {
        Record("chunks-reducer", cancellationToken);
        ReduceRequest = request;
        return Task.FromResult(new ReduceResult(Summary));
    }

    public Task<KeywordsResult> ExtractKeywordsAsync(ReduceResult request, CancellationToken cancellationToken = default)
    {
        Record("extract-keywords", cancellationToken);
        KeywordsRequest = request;
        return Task.FromResult(new KeywordsResult(Keywords));
    }

    private void Record(string call, CancellationToken cancellationToken)
    {
        // Honoured so the runner's shutdown path can be tested: a real HTTP call would abort here too.
        cancellationToken.ThrowIfCancellationRequested();

        Calls.Add(call);
        if (FailOnCall == call)
        {
            throw new N8nException($"n8n webhook '{call}' returned {(int)FailureStatusCode}: boom")
            {
                StatusCode = FailureStatusCode,
            };
        }
    }
}

public sealed class FakeVideoTitleResolver(string? title = "Daggerheart combat is broken") : IVideoTitleResolver
{
    public List<string> RequestedUrls { get; } = [];

    public Task<string?> ResolveTitleAsync(string canonicalUrl, CancellationToken cancellationToken = default)
    {
        RequestedUrls.Add(canonicalUrl);
        return Task.FromResult(title);
    }
}

public sealed class FakeChatReplySender : Core.Notifications.IChatReplySender
{
    public List<(long ChatId, string Text)> Sent { get; } = [];

    public Task SendAsync(long chatId, string text, CancellationToken cancellationToken = default)
    {
        Sent.Add((chatId, text));
        return Task.CompletedTask;
    }
}
