using MindBot.Core.YouTube;
using MindBot.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace MindBot.Tests;

public sealed class YouTubeSummaryPipelineTests
{
    private static YouTubeSummaryPipeline CreatePipeline(FakeN8nClient client, IVideoTitleResolver? titleResolver = null) =>
        new(client, titleResolver ?? new FakeVideoTitleResolver(), NullLogger<YouTubeSummaryPipeline>.Instance);

    [Fact]
    public async Task CallsTheFiveWebhooksInOrder()
    {
        var client = new FakeN8nClient();

        await CreatePipeline(client).RunAsync("qIeJ7Gw9v_I", requestedChunkCount: 2);

        Assert.Equal(
            ["get-yt-transcript", "text-chunker", "summarize-chunks", "chunks-reducer", "extract-keywords"],
            client.Calls);
    }

    [Fact]
    public async Task PassesAnExplicitChunkCountThrough()
    {
        var client = new FakeN8nClient();

        await CreatePipeline(client).RunAsync("qIeJ7Gw9v_I", requestedChunkCount: 7);

        Assert.Equal(7, client.ChunkerRequest!.ChunksNumber);
    }

    [Fact]
    public async Task DerivesTheChunkCountFromTheTranscriptWhenNoneWasGiven()
    {
        var client = new FakeN8nClient
        {
            // 500 segments x 6 words = 3000 words.
            Transcript = new TranscriptResult(
                "long",
                Enumerable.Range(0, 500).Select(i => new TranscriptSegment("one two three four five six", i * 2, 2)).ToList(),
                "qIeJ7Gw9v_I",
                "https://youtube.com/watch?v=qIeJ7Gw9v_I"),
        };

        await CreatePipeline(client).RunAsync("qIeJ7Gw9v_I", requestedChunkCount: null);

        Assert.Equal(ChunkCountCalculator.ForWordCount(3000), client.ChunkerRequest!.ChunksNumber);
    }

    [Fact]
    public async Task ForwardsEachStagesOutputToTheNextUnchanged()
    {
        var client = new FakeN8nClient();

        await CreatePipeline(client).RunAsync("qIeJ7Gw9v_I", requestedChunkCount: 3);

        Assert.Equal(client.Transcript.Segments, client.ChunkerRequest!.Segments);
        Assert.Equal(3, client.SummarizeRequest!.NumChunks);
        Assert.Equal(3, client.ReduceRequest!.ChunkSummaries.Count);
        Assert.Equal(client.Summary, client.KeywordsRequest!.Summary);
    }

    [Fact]
    public async Task ResolvesTheTitleFromTheCanonicalUrl()
    {
        var client = new FakeN8nClient();
        var resolver = new FakeVideoTitleResolver("Daggerheart combat is broken");

        var result = await CreatePipeline(client, resolver).RunAsync("qIeJ7Gw9v_I", requestedChunkCount: 1);

        Assert.Equal(["https://www.youtube.com/watch?v=qIeJ7Gw9v_I"], resolver.RequestedUrls);
        Assert.Equal("Daggerheart combat is broken", result.Title);
    }

    [Fact]
    public async Task FallsBackToTheVideoIdWhenTheTitleCannotBeResolved()
    {
        var result = await CreatePipeline(new FakeN8nClient(), new FakeVideoTitleResolver(title: null))
            .RunAsync("qIeJ7Gw9v_I", requestedChunkCount: 1);

        Assert.Equal("qIeJ7Gw9v_I", result.Title);
    }

    [Fact]
    public async Task UsesTheCanonicalUrlForTheNoteSourceLink()
    {
        var result = await CreatePipeline(new FakeN8nClient()).RunAsync("qIeJ7Gw9v_I", requestedChunkCount: 1);

        Assert.Equal("https://www.youtube.com/watch?v=qIeJ7Gw9v_I", result.VideoUrl);
    }

    /// <summary>A video with captions disabled comes back 200 with nothing in it; that must be a named failure.</summary>
    [Fact]
    public async Task ThrowsWhenTheTranscriptHasNoSegments()
    {
        var client = new FakeN8nClient
        {
            Transcript = new TranscriptResult("", [], "qIeJ7Gw9v_I", "https://youtube.com/watch?v=qIeJ7Gw9v_I"),
        };

        var exception = await Assert.ThrowsAsync<N8nException>(() =>
            CreatePipeline(client).RunAsync("qIeJ7Gw9v_I", requestedChunkCount: null));

        Assert.Contains("no segments", exception.Message);
        Assert.Equal(["get-yt-transcript"], client.Calls);
    }
}
