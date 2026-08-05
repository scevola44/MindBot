using System.Net.Http.Json;
using System.Text.Json;
using MindBot.Core.YouTube;

namespace MindBot.Infrastructure.N8n;

/// <summary>
/// Talks to the five n8n webhooks over HTTP.
/// <para>
/// Every one of them answers with a single-element JSON array — that is how an n8n workflow returns
/// its last node's item list — so <see cref="UnwrapAsync{T}"/> is applied uniformly rather than each
/// call inventing its own unwrapping.
/// </para>
/// <para>
/// No retry policy here on purpose. A retry belongs at the job level, where it can survive a
/// process restart and back off over minutes; retrying a ten-minute LLM call inside the HTTP
/// handler would just multiply the timeout.
/// </para>
/// </summary>
public sealed class N8nHttpClient(HttpClient httpClient) : IN8nClient
{
    private const string TranscriptPath = "get-yt-transcript";
    private const string ChunkerPath = "text-chunker";
    private const string SummarizePath = "summarize-chunks";
    private const string ReducerPath = "chunks-reducer";
    private const string KeywordsPath = "extract-keywords";

    // The contracts carry explicit [JsonPropertyName] attributes, so no naming policy is applied
    // here -- adding one would silently rename anything that ever loses its attribute.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null,
    };

    public Task<TranscriptResult> GetTranscriptAsync(TranscriptRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<TranscriptRequest, TranscriptResult>(TranscriptPath, request, cancellationToken);

    public Task<ChunkerResult> ChunkTextAsync(ChunkerRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<ChunkerRequest, ChunkerResult>(ChunkerPath, request, cancellationToken);

    public Task<SummarizeResult> SummarizeChunksAsync(ChunkerResult request, CancellationToken cancellationToken = default) =>
        PostAsync<ChunkerResult, SummarizeResult>(SummarizePath, request, cancellationToken);

    public Task<ReduceResult> ReduceChunksAsync(SummarizeResult request, CancellationToken cancellationToken = default) =>
        PostAsync<SummarizeResult, ReduceResult>(ReducerPath, request, cancellationToken);

    public Task<KeywordsResult> ExtractKeywordsAsync(ReduceResult request, CancellationToken cancellationToken = default) =>
        PostAsync<ReduceResult, KeywordsResult>(KeywordsPath, request, cancellationToken);

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsJsonAsync(path, request, Json, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new N8nException($"n8n webhook '{path}' could not be reached: {ex.Message}", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new N8nException($"n8n webhook '{path}' returned {(int)response.StatusCode}: {Truncate(body)}");
            }

            return await UnwrapAsync<TResponse>(path, response, cancellationToken);
        }
    }

    /// <summary>
    /// Reads the single item out of the workflow's response array. A workflow whose last node
    /// filtered everything out returns "[]" with a 200, which would otherwise surface as a
    /// NullReferenceException minutes later instead of a named failure here.
    /// </summary>
    private static async Task<T> UnwrapAsync<T>(string path, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        T[]? items;
        try
        {
            items = await response.Content.ReadFromJsonAsync<T[]>(Json, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new N8nException($"n8n webhook '{path}' returned a body this client cannot read: {ex.Message}", ex);
        }

        if (items is null || items.Length == 0)
        {
            throw new N8nException($"n8n webhook '{path}' returned an empty result.");
        }

        return items[0];
    }

    private static string Truncate(string body) =>
        body.Length <= 500 ? body : $"{body[..500]}...";
}
