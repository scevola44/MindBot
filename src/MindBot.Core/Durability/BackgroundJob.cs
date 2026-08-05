namespace MindBot.Core.Durability;

public enum BackgroundJobStatus
{
    Pending = 0,
    Completed = 1,
    Failed = 2,
}

/// <summary>
/// Work a command accepted but could not finish inside the ingest transaction — today, the
/// /ytsummary n8n pipeline, which takes minutes.
/// <para>
/// The row is inserted in the same transaction that marks the Telegram update processed, so the
/// duplicate guard covers deferred work exactly as it covers write jobs: a crash before the commit
/// means Telegram redelivers and the job is created once; a crash after it means the job survives
/// the restart and is retried from its own record.
/// </para>
/// </summary>
public sealed record BackgroundJob(
    long Id,
    long UpdateId,
    string Kind,
    string Payload,
    long ChatId,
    long SenderId,
    BackgroundJobStatus Status,
    int Attempts,
    string? LastError,
    DateTimeOffset EnqueuedAt,
    DateTimeOffset NextAttemptAt);

/// <summary>Well-known <see cref="BackgroundJob.Kind"/> values, so producer and worker cannot drift apart.</summary>
public static class BackgroundJobKinds
{
    public const string YouTubeSummary = "youtube-summary";
}
