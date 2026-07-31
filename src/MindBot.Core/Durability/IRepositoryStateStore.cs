namespace MindBot.Core.Durability;

public sealed record RepositoryState(
    string? LastPushedSha,
    int LastTelegramOffset,
    DateTimeOffset? LastSuccessfulPushAt);

/// <summary>
/// The small amount of cross-restart git state the bot must remember. <c>LastPushedSha</c> is what
/// lets the classifier tell "the operator advanced the branch" apart from "the operator rewrote
/// it after triage" — without it, the bot cannot safely decide whether replaying its un-pushed
/// commits would resurrect already-processed notes.
/// </summary>
public interface IRepositoryStateStore
{
    Task<RepositoryState> GetAsync(CancellationToken cancellationToken = default);

    Task SetLastPushedShaAsync(string sha, DateTimeOffset pushedAt, CancellationToken cancellationToken = default);
}
