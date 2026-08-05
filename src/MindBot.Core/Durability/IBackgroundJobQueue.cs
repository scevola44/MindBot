namespace MindBot.Core.Durability;

/// <summary>
/// Read/complete side of the durable background-job queue, used by the worker. The enqueue side
/// lives on <see cref="IIngestUnitOfWork"/> instead, because a job must be created in the same
/// transaction that accepts the update that asked for it.
/// <para>
/// Completion is on <see cref="IIngestUnitOfWork"/> for the mirror-image reason: a job is done
/// exactly when the note it produced is durably queued, and splitting those into two transactions
/// would let a crash between them replay the whole pipeline and file the note twice.
/// </para>
/// </summary>
public interface IBackgroundJobQueue
{
    /// <summary>
    /// The oldest pending job of <paramref name="kind"/> whose backoff has elapsed, or null when
    /// there is nothing to do. Does not lock the row: this bot runs as a single instance with one
    /// worker loop, so the only contention would be with itself.
    /// </summary>
    Task<BackgroundJob?> GetNextPendingAsync(string kind, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Records a failed attempt and schedules the retry.</summary>
    Task RecordFailureAsync(long jobId, string error, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken = default);

    /// <summary>Gives up on a job permanently; it is never claimed again.</summary>
    Task MarkFailedAsync(long jobId, string error, CancellationToken cancellationToken = default);
}
