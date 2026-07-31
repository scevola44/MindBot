namespace MindBot.Core.Options;

/// <summary>Bound from the GIT__ environment variable prefix.</summary>
public sealed class GitOptions
{
    public const string SectionName = "GIT";

    public string RemoteUrl { get; set; } = string.Empty;

    /// <summary>The bot-owned branch it exclusively reads from and writes to (e.g. bot-inbox).</summary>
    public string Branch { get; set; } = string.Empty;

    /// <summary>Path to the mounted SSH private key used to authenticate against the remote.</summary>
    public string SshKeyPath { get; set; } = string.Empty;

    /// <summary>Path to a known_hosts file. Falls back to ssh's default if not set.</summary>
    public string? KnownHostsPath { get; set; }

    public string UserName { get; set; } = "MindBot";

    public string UserEmail { get; set; } = "mindbot@localhost";

    /// <summary>
    /// Directory where recovery bundles are written when the operator rewrites the branch out
    /// from under un-pushed commits. Must live outside <see cref="VaultOptions.Root"/>, or the
    /// bundle would be committed onto the branch it is meant to rescue commits from.
    /// </summary>
    public string RecoveryPath { get; set; } = "/data/recovery";

    /// <summary>
    /// How long the drain worker waits after the first queued job before committing, so a burst
    /// of messages coalesces into one commit. Measured from the first job's arrival, not slid
    /// forward on each new arrival, so a sustained burst cannot defer the commit indefinitely.
    /// </summary>
    public int BatchWindowSeconds { get; set; } = 5;

    /// <summary>Maximum jobs folded into a single commit, bounding worst-case commit size.</summary>
    public int MaxBatchSize { get; set; } = 100;

    /// <summary>Number of push attempts before the bot gives up and reports a degraded state.</summary>
    public int PushRetryCount { get; set; } = 3;

    /// <summary>Base delay for the exponential push backoff (2s, 4s, 8s, ...).</summary>
    public int PushRetryBaseSeconds { get; set; } = 2;
}
