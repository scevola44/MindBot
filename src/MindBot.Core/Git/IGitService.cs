namespace MindBot.Core.Git;

/// <summary>
/// Drives the git CLI. Each method is one logical operation and holds the repository lock for
/// its whole body: the bot is the only writer to its configured branch and must never leave a
/// multi-step sequence (classify, then pull) open to interleaving.
/// </summary>
public interface IGitService
{
    /// <summary>
    /// Clones the repository if the vault root is empty. Then, if the configured branch already
    /// exists locally, simply checks it out (never resetting it to origin, so commits made
    /// locally but not yet pushed survive a restart). Otherwise checks it out from origin if it
    /// exists there, or creates it from the default branch and pushes it.
    /// Safe to call repeatedly (e.g. on every startup).
    /// </summary>
    Task<GitOperationResult> EnsureRepositoryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the pre-write classification and the synchronisation it implies. Never throws and
    /// never fails the caller: when the remote is unreachable the result says so and the caller
    /// proceeds with a local-only write.
    /// </summary>
    /// <param name="lastPushedSha">
    /// The SHA this bot last successfully pushed, or null if it has never recorded one. Null is
    /// treated as <see cref="GitSyncStrategy.OperatorAdvanced"/>: with no recorded push the local
    /// commits were never pushed, so they were never triaged and replaying them is safe.
    /// </param>
    Task<GitClassification> SynchronizeAsync(string? lastPushedSha, CancellationToken cancellationToken = default);

    /// <summary>Stages everything and creates one commit. A no-op tree is reported as success.</summary>
    Task<GitOperationResult> CommitAsync(string message, CancellationToken cancellationToken = default);

    /// <summary>Pushes the configured branch, distinguishing a non-fast-forward rejection from a network failure.</summary>
    Task<GitPushResult> PushAsync(CancellationToken cancellationToken = default);

    /// <summary>Read-only working-tree and un-pushed-commit state, for the health endpoint.</summary>
    Task<GitStatusSnapshot> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Current HEAD SHA, recorded as lastPushedSha after a successful push.</summary>
    Task<string?> GetHeadShaAsync(CancellationToken cancellationToken = default);

    /// <summary>Dry-run push used at startup to confirm the branch is writable before the poller starts.</summary>
    Task<GitOperationResult> VerifyRemoteWritableAsync(CancellationToken cancellationToken = default);
}
