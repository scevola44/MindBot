namespace MindBot.Core.YouTube;

/// <summary>
/// Looks up a video's human-readable title, which none of the n8n endpoints return.
/// <para>
/// Returns null rather than throwing on any failure: the title is what makes the note readable, but
/// a summary that took minutes to produce must never be discarded because a metadata lookup was
/// unavailable.
/// </para>
/// </summary>
public interface IVideoTitleResolver
{
    Task<string?> ResolveTitleAsync(string canonicalUrl, CancellationToken cancellationToken = default);
}
