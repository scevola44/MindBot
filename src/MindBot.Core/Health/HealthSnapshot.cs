using MindBot.Core.Git;

namespace MindBot.Core.Health;

/// <summary>Point-in-time view of the bot's operational state, as returned by /health.</summary>
public sealed record HealthReport(
    DateTimeOffset? LastSuccessfulPollAt,
    DateTimeOffset? LastSuccessfulPushAt,
    int QueueDepth,
    int UnpushedCommitCount,
    bool WorkingTreeDirty,
    string? LastClassification)
{
    /// <summary>
    /// The core invariant: the bot pushes immediately after each commit and normally holds zero
    /// un-pushed commits. Anything else means the rarely-exercised recovery paths are live.
    /// </summary>
    public bool Degraded => UnpushedCommitCount > 0 || WorkingTreeDirty;
}

/// <summary>
/// Mutable, thread-safe health state written by the ingest loop and the drain worker and read by
/// the health endpoint.
/// <para>
/// The endpoint reads this cache rather than shelling out to git per probe: a healthcheck every
/// 30 seconds that took the repository lock would contend with the drain worker for no benefit.
/// The drain worker refreshes the git fields on an idle timer so the cache stays honest during
/// long quiet periods.
/// </para>
/// </summary>
public sealed class HealthSnapshot
{
    private readonly Lock _gate = new();

    private DateTimeOffset? _lastSuccessfulPollAt;
    private DateTimeOffset? _lastSuccessfulPushAt;
    private int _queueDepth;
    private int _unpushedCommitCount;
    private bool _workingTreeDirty;
    private string? _lastClassification;

    public void RecordSuccessfulPoll(DateTimeOffset at)
    {
        lock (_gate)
        {
            _lastSuccessfulPollAt = at;
        }
    }

    public void RecordSuccessfulPush(DateTimeOffset at)
    {
        lock (_gate)
        {
            _lastSuccessfulPushAt = at;
        }
    }

    public void RecordQueueDepth(int depth)
    {
        lock (_gate)
        {
            _queueDepth = depth;
        }
    }

    public void RecordGitStatus(GitStatusSnapshot status)
    {
        lock (_gate)
        {
            _workingTreeDirty = status.WorkingTreeDirty;
            _unpushedCommitCount = status.UnpushedCommitCount;
        }
    }

    public void RecordClassification(GitSyncStrategy strategy)
    {
        lock (_gate)
        {
            _lastClassification = strategy.ToString();
        }
    }

    public HealthReport Read()
    {
        lock (_gate)
        {
            return new HealthReport(
                _lastSuccessfulPollAt,
                _lastSuccessfulPushAt,
                _queueDepth,
                _unpushedCommitCount,
                _workingTreeDirty,
                _lastClassification);
        }
    }
}
