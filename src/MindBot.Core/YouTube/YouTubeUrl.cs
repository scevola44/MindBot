using System.Text.RegularExpressions;

namespace MindBot.Core.YouTube;

/// <summary>
/// Extracts the video id from the URL forms a phone actually shares — a youtu.be short link with a
/// tracking "si" parameter, a Shorts link, a watch URL with a playlist and a timestamp attached.
/// <para>
/// The id is the only thing carried forward: everything downstream (n8n, oEmbed, the note's source
/// link) gets the canonical watch URL rebuilt from it, so a shared link's tracking parameters never
/// reach an external service or the vault.
/// </para>
/// </summary>
public static partial class YouTubeUrl
{
    /// <summary>YouTube video ids are exactly 11 characters from the URL-safe base64 alphabet.</summary>
    [GeneratedRegex("^[A-Za-z0-9_-]{11}$")]
    private static partial Regex VideoId();

    /// <summary>Returns the 11-character video id, or null when the text is not a YouTube video link.</summary>
    public static string? TryParseVideoId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();

        // A bare id is accepted so a user can paste just the id back from a previous note.
        if (VideoId().IsMatch(trimmed))
        {
            return trimmed;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && !Uri.TryCreate($"https://{trimmed}", UriKind.Absolute, out uri))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        var candidate = host.ToLowerInvariant() switch
        {
            "youtu.be" => segments.FirstOrDefault(),
            "youtube.com" or "m.youtube.com" or "music.youtube.com" or "youtube-nocookie.com" => FromYouTubePath(uri, segments),
            _ => null,
        };

        return candidate is not null && VideoId().IsMatch(candidate) ? candidate : null;
    }

    /// <summary>The canonical watch URL for a video id — the only form sent anywhere.</summary>
    public static string CanonicalUrl(string videoId) => $"https://www.youtube.com/watch?v={videoId}";

    private static string? FromYouTubePath(Uri uri, string[] segments)
    {
        if (segments.Length == 0)
        {
            return null;
        }

        // /shorts/<id>, /live/<id>, /embed/<id>, /v/<id> all put the id in the second segment.
        if (segments.Length >= 2
            && segments[0].ToLowerInvariant() is "shorts" or "live" or "embed" or "v")
        {
            return segments[1];
        }

        return segments[0].Equals("watch", StringComparison.OrdinalIgnoreCase)
            ? QueryValue(uri.Query, "v")
            : null;
    }

    /// <summary>
    /// Hand-rolled rather than HttpUtility.ParseQueryString: that lives in System.Web, which this
    /// project does not reference, and the ids we care about need no unescaping.
    /// </summary>
    private static string? QueryValue(string query, string key)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator > 0 && pair[..separator].Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pair[(separator + 1)..]);
            }
        }

        return null;
    }
}
