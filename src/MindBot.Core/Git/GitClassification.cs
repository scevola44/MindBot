namespace MindBot.Core.Git;

/// <summary>
/// How the local branch relates to origin at the moment a batch is about to be written.
/// Classification runs before every pull; only <see cref="FastForward"/> is the normal path.
/// </summary>
public enum GitSyncStrategy
{
    /// <summary>
    /// Case 1 — no local commits unreachable from origin. The healthy steady state: the bot
    /// pushes immediately after each commit, so every pull is a fast-forward and conflicts
    /// cannot occur.
    /// </summary>
    FastForward,

    /// <summary>
    /// Case 2 — the bot holds un-pushed commits and the last SHA it pushed is still an ancestor
    /// of origin. The operator advanced the branch (merged main in, or added commits) without
    /// discarding the bot's history, so replaying the un-pushed commits on top is safe.
    /// </summary>
    OperatorAdvanced,

    /// <summary>
    /// Case 3 — the bot holds un-pushed commits and the last SHA it pushed is NOT an ancestor of
    /// origin: the branch was reset or rewritten after triage. Rebasing here would replay commits
    /// whose notes the operator has already processed, resurrecting deleted notes. The un-pushed
    /// commits are exported to a recovery bundle and the branch is reset to origin instead.
    /// </summary>
    RemoteRewritten,

    /// <summary>
    /// Case 4 — fetch failed for network reasons. The write proceeds locally regardless: a
    /// capture is never dropped because the remote is down.
    /// </summary>
    RemoteUnreachable,
}

/// <summary>The outcome of a pre-write classification and the synchronisation it performed.</summary>
/// <param name="Strategy">Which of the four cases applied.</param>
/// <param name="UnpushedCommitCount">Commits held locally but not reachable from origin, measured before synchronising.</param>
/// <param name="WorkingTreeWasDirty">
/// True when an unclean shutdown left uncommitted note content that had to be committed before
/// classification could proceed. Always a warning-level event.
/// </param>
/// <param name="RecoveryBundlePath">Set only for <see cref="GitSyncStrategy.RemoteRewritten"/>.</param>
/// <param name="RecoveredCommitCount">Number of commits captured in the recovery bundle.</param>
/// <param name="Error">Set when the strategy could not be carried out; the caller still writes locally.</param>
public sealed record GitClassification(
    GitSyncStrategy Strategy,
    int UnpushedCommitCount,
    bool WorkingTreeWasDirty,
    string? RecoveryBundlePath = null,
    int RecoveredCommitCount = 0,
    string? Error = null);

/// <summary>Cheap repository state used by the health endpoint. Never mutates the repository.</summary>
public sealed record GitStatusSnapshot(bool WorkingTreeDirty, int UnpushedCommitCount)
{
    public static GitStatusSnapshot Unknown { get; } = new(false, 0);
}

/// <summary>Why a push did not succeed. The two cases need different handling.</summary>
public enum GitPushFailure
{
    None,

    /// <summary>
    /// The remote rejected the push as non-fast-forward. Re-classify and retry — the remote moved
    /// under us, which is a state question, not a connectivity one.
    /// </summary>
    Rejected,

    /// <summary>The remote could not be reached. Back off and retry; the local commit stands.</summary>
    Network,
}

public sealed record GitPushResult(bool Success, GitPushFailure Failure, string? ErrorMessage)
{
    public static GitPushResult Ok { get; } = new(true, GitPushFailure.None, null);

    public static GitPushResult Rejected(string error) => new(false, GitPushFailure.Rejected, error);

    public static GitPushResult NetworkError(string error) => new(false, GitPushFailure.Network, error);
}
