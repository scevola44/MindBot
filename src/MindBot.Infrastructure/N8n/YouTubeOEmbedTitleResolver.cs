using System.Text.Json;
using MindBot.Core.YouTube;
using Microsoft.Extensions.Logging;

namespace MindBot.Infrastructure.N8n;

/// <summary>
/// Resolves a video title through YouTube's public oEmbed endpoint: no API key, no quota, and no
/// account of ours attached to the request.
/// <para>
/// Never throws. A title is cosmetic, and by the time it is fetched the summary has already cost
/// minutes of LLM time — losing that over a metadata lookup would be absurd. The caller falls back
/// to the video id.
/// </para>
/// </summary>
public sealed class YouTubeOEmbedTitleResolver(HttpClient httpClient, ILogger<YouTubeOEmbedTitleResolver> logger) : IVideoTitleResolver
{
    private const string OEmbedEndpoint = "https://www.youtube.com/oembed";

    public async Task<string?> ResolveTitleAsync(string canonicalUrl, CancellationToken cancellationToken = default)
    {
        var requestUri = $"{OEmbedEndpoint}?url={Uri.EscapeDataString(canonicalUrl)}&format=json";

        try
        {
            using var response = await httpClient.GetAsync(requestUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "YouTube oEmbed returned {StatusCode} for {Url}; falling back to the video id for the note title.",
                    (int)response.StatusCode,
                    canonicalUrl);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            return document.RootElement.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String
                ? title.GetString()
                : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not resolve a YouTube title for {Url}; falling back to the video id.", canonicalUrl);
            return null;
        }
    }
}
