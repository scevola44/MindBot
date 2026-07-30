namespace MindBot.Core.Git;

/// <summary>
/// All operations are serialized behind a single semaphore by the implementation:
/// the bot is the only writer to its configured branch and must never run two
/// git invocations against the working tree concurrently.
/// </summary>
public interface IGitService
{
    /// <summary>
    /// Clones the repository if the vault root is empty, then checks out the configured
    /// branch from origin if it exists, or creates it from the default branch and pushes it.
    /// Safe to call repeatedly (e.g. on every startup).
    /// </summary>
    Task<GitOperationResult> EnsureRepositoryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebases onto the latest remote state. Never throws: a failure (e.g. remote unreachable)
    /// is reported in the result so the caller can continue with a local-only write.
    /// </summary>
    Task<GitOperationResult> PullAsync(CancellationToken cancellationToken = default);

    Task<GitOperationResult> CommitAsync(string message, CancellationToken cancellationToken = default);

    Task<GitOperationResult> PushAsync(CancellationToken cancellationToken = default);

    /// <summary>Dry-run push used at startup to confirm the branch is writable before the poller starts.</summary>
    Task<GitOperationResult> VerifyRemoteWritableAsync(CancellationToken cancellationToken = default);
}
