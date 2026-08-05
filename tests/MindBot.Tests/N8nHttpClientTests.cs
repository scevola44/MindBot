using System.Net;
using System.Text;
using System.Text.Json;
using MindBot.Core.YouTube;
using MindBot.Infrastructure.N8n;

namespace MindBot.Tests;

public sealed class N8nHttpClientTests
{
    /// <summary>Captures the outgoing request and replays a canned body, so the wire format is asserted directly.</summary>
    private sealed class StubHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static (N8nHttpClient Client, StubHandler Handler) Create(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new StubHandler(statusCode, responseBody);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://n8n.example/webhook/") };
        return (new N8nHttpClient(httpClient), handler);
    }

    [Fact]
    public async Task PostsToTheWebhookPathUnderTheBaseAddress()
    {
        var (client, handler) = Create("""[{"transcript":"t","segments":[],"video_id":"v","video_url":"u"}]""");

        await client.GetTranscriptAsync(new TranscriptRequest("https://www.youtube.com/watch?v=qIeJ7Gw9v_I"));

        Assert.Equal("https://n8n.example/webhook/get-yt-transcript", handler.RequestUri!.ToString());
    }

    /// <summary>
    /// The hyphen makes this key unreachable by any JSON naming policy, so it is pinned literally:
    /// misspelling it would silently give n8n a null chunk count.
    /// </summary>
    [Fact]
    public async Task SendsTheChunkCountUnderTheHyphenatedKey()
    {
        var (client, handler) = Create("""[{"num_chunks":2,"chunks":[]}]""");
        var segments = new[] { new TranscriptSegment("hello there", 1.5, 2.25) };

        await client.ChunkTextAsync(new ChunkerRequest(segments, 4));

        using var document = JsonDocument.Parse(handler.RequestBody);
        Assert.Equal(4, document.RootElement.GetProperty("chunks-number").GetInt32());

        var segment = document.RootElement.GetProperty("segments")[0];
        Assert.Equal("hello there", segment.GetProperty("text").GetString());
        Assert.Equal(1.5, segment.GetProperty("start").GetDouble());
        Assert.Equal(2.25, segment.GetProperty("duration").GetDouble());
    }

    [Fact]
    public async Task ParsesTheSampleTranscriptResponse()
    {
        var (client, _) = Create(
            """
            [{"transcript":"the whole thing",
              "segments":[{"text":"Some articles we've acquired from the","start":0,"duration":5.76}],
              "video_id":"qIeJ7Gw9v_I",
              "video_url":"https://youtube.com/watch?v=qIeJ7Gw9v_I"}]
            """);

        var result = await client.GetTranscriptAsync(new TranscriptRequest("https://www.youtube.com/watch?v=qIeJ7Gw9v_I"));

        Assert.Equal("qIeJ7Gw9v_I", result.VideoId);
        Assert.Equal("the whole thing", result.Transcript);
        var segment = Assert.Single(result.Segments);
        Assert.Equal("Some articles we've acquired from the", segment.Text);
        Assert.Equal(5.76, segment.Duration);
    }

    [Fact]
    public async Task ParsesTheSampleChunkerResponse()
    {
        var (client, _) = Create(
            """
            [{"num_chunks":2,"chunks":[
              {"chunk_index":0,"text":"first","start_time":0,"end_time":506.6,"word_count":1510},
              {"chunk_index":1,"text":"second","start_time":426.28,"end_time":982.8,"word_count":1727}]}]
            """);

        var result = await client.ChunkTextAsync(new ChunkerRequest([], 2));

        Assert.Equal(2, result.NumChunks);
        Assert.Equal(1727, result.Chunks[1].WordCount);
        Assert.Equal(426.28, result.Chunks[1].StartTime);
    }

    [Fact]
    public async Task ParsesTheSampleSummarizeResponseWithItsStringTimestamps()
    {
        var (client, _) = Create(
            """
            [{"num_chunks":2,"chunk_summaries":[
              {"chunk_index":0,"start_time":"0:00","end_time":"8:26","summary":"Aura threatening."},
              {"chunk_index":1,"start_time":"7:06","end_time":"16:22","summary":"What happens next."}]}]
            """);

        var result = await client.SummarizeChunksAsync(new ChunkerResult(2, []));

        Assert.Equal("8:26", result.ChunkSummaries[0].EndTime);
        Assert.Equal("What happens next.", result.ChunkSummaries[1].Summary);
    }

    [Fact]
    public async Task ParsesTheSampleReducerAndKeywordResponses()
    {
        var (reducer, _) = Create("""[{"summary":"## Summary\nDaggerheart combat."}]""");
        Assert.Equal("## Summary\nDaggerheart combat.", (await reducer.ReduceChunksAsync(new SummarizeResult(1, []))).Summary);

        var (keywords, _) = Create("""[{"keywords":["Daggerheart","Dark Heresy"]}]""");
        Assert.Equal(["Daggerheart", "Dark Heresy"], (await keywords.ExtractKeywordsAsync(new ReduceResult("s"))).Keywords);
    }

    /// <summary>A workflow whose last node filtered everything out answers 200 with "[]".</summary>
    [Fact]
    public async Task AnEmptyResultArrayIsANamedFailure()
    {
        var (client, _) = Create("[]");

        var exception = await Assert.ThrowsAsync<N8nException>(() => client.ExtractKeywordsAsync(new ReduceResult("s")));

        Assert.Contains("extract-keywords", exception.Message);
        Assert.Contains("empty result", exception.Message);
    }

    [Fact]
    public async Task ANonSuccessStatusIsANamedFailureCarryingTheBody()
    {
        var (client, _) = Create("workflow not active", HttpStatusCode.NotFound);

        var exception = await Assert.ThrowsAsync<N8nException>(() =>
            client.GetTranscriptAsync(new TranscriptRequest("https://www.youtube.com/watch?v=qIeJ7Gw9v_I")));

        Assert.Contains("get-yt-transcript", exception.Message);
        Assert.Contains("404", exception.Message);
        Assert.Contains("workflow not active", exception.Message);
    }

    [Fact]
    public async Task AnUnreadableBodyIsANamedFailure()
    {
        var (client, _) = Create("""{"not":"an array"}""");

        await Assert.ThrowsAsync<N8nException>(() => client.ReduceChunksAsync(new SummarizeResult(1, [])));
    }
}
