using System.Net.Http.Json;
using System.Text.Json;
using MindBot.Core.YouTube;

namespace MindBot.Infrastructure.N8n;

/// <summary>
/// Talks to the five n8n webhooks over HTTP.
/// <para>
/// Each workflow's "Respond to Webhook" node is configured per-workflow to answer either with the
/// full item list (a JSON array) or with just the last node's first item (a bare JSON object) —
/// n8n calls these "All Entries" and "First Entry JSON" respectively, and nothing on this side
/// controls which one a given workflow uses. <see cref="UnwrapAsync{T}"/> accepts either shape
/// uniformly rather than each call inventing its own unwrapping.
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
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested && ex.InnerException is TimeoutException)
        {
            throw new N8nException(
                $"n8n webhook '{path}' timed out after {httpClient.Timeout.TotalSeconds:0}s (N8N__TIMEOUTSECONDS).", ex);
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
    /// Reads the single result out of the workflow's response, whether it is a JSON array (take the
    /// first item) or a bare JSON object (use it directly) — n8n's "Respond to Webhook" node can be
    /// configured either way per-workflow. A workflow whose last node filtered everything out answers
    /// "[]", and the bare-object equivalent is a literal "null" body; both would otherwise surface as
    /// a NullReferenceException minutes later instead of a named failure here.
    /// </summary>
    private static async Task<T> UnwrapAsync<T>(string path, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        T? result;
        try
        {
            using var document = JsonDocument.Parse(body);
            result = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().Select(element => element.Deserialize<T>(Json)).FirstOrDefault()
                : document.RootElement.Deserialize<T>(Json);
        }
        catch (JsonException ex)
        {
            throw new N8nException($"n8n webhook '{path}' returned a body this client cannot read: {ex.Message} | Body: {Truncate(body)}", ex);
        }

        if (result is null)
        {
            throw new N8nException($"n8n webhook '{path}' returned an empty result.");
        }

        return result;
    }

    private static string Truncate(string body) =>
        body.Length <= 500 ? body : $"{body[..500]}...";
}
