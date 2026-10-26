using MindBot.Core.Durability;
using MindBot.Core.Options;
using Microsoft.Extensions.Options;

namespace MindBot.Core.Health;

/// <summary>Mirrors the /health HTTP endpoint's JSON payload field-for-field.</summary>
public sealed record HealthReportPayload(
    string Status,
    bool Degraded,
    DateTimeOffset? LastSuccessfulPollUtc,
    DateTimeOffset? LastSuccessfulPushUtc,
    int QueueDepth,
    int UnpushedCommitCount,
    bool WorkingTreeDirty,
    string? LastClassification,
    bool StateStoreReachable,
    bool PollStalled,
    bool TelegramEnabled);

/// <summary>
/// Builds the same health payload the /health endpoint serves, extracted so both the HTTP endpoint
/// and the /status chat command share one source of truth. Only reads an in-memory snapshot and
/// probes the write-job queue -- never touches git or the filesystem, so a /status reply can never
/// trigger either.
/// </summary>
public sealed class HealthReportService(
    HealthSnapshot health,
    IWriteJobQueue jobQueue,
    TimeProvider timeProvider,
    IOptions<TelegramOptions>? telegramOptions = null)
{
    // Null only in tests that predate optional Telegram, which all assumed it was on.
    private readonly bool _telegramEnabled = telegramOptions?.Value.IsEnabled ?? true;

    // 3x the 30s long-poll timeout: one missed cycle is normal, three is not.
    private static readonly TimeSpan PollStallThreshold = TimeSpan.FromSeconds(180);

    private readonly DateTimeOffset _processStartedAt = timeProvider.GetUtcNow();

    public async Task<HealthReportPayload> BuildAsync(CancellationToken cancellationToken = default)
    {
        var report = health.Read();
        var now = timeProvider.GetUtcNow();

        bool stateStoreReachable;
        try
        {
            await jobQueue.GetPendingCountAsync(cancellationToken);
            stateStoreReachable = true;
        }
        catch (Exception)
        {
            stateStoreReachable = false;
        }

        // With Telegram off there is no poller, and "no poll for three minutes" is just the normal state.
        var lastPollOrStartup = report.LastSuccessfulPollAt ?? _processStartedAt;
        var pollStalled = _telegramEnabled && now - lastPollOrStartup > PollStallThreshold;
        var healthy = stateStoreReachable && !pollStalled;

        return new HealthReportPayload(
            healthy ? "healthy" : "unhealthy",
            report.Degraded,
            report.LastSuccessfulPollAt,
            report.LastSuccessfulPushAt,
            report.QueueDepth,
            report.UnpushedCommitCount,
            report.WorkingTreeDirty,
            report.LastClassification,
            stateStoreReachable,
            pollStalled,
            _telegramEnabled);
    }
}
