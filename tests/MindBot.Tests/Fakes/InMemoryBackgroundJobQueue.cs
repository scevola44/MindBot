using MindBot.Core.Durability;

namespace MindBot.Tests.Fakes;

/// <summary>
/// In-memory stand-in for the SQLite background-job queue. Mirrors the real claim rule: pending
/// jobs of one kind, oldest first, skipping any whose retry backoff has not elapsed.
/// </summary>
public sealed class InMemoryBackgroundJobQueue : IBackgroundJobQueue
{
    private readonly List<BackgroundJob> _jobs = [];
    private long _nextId = 1;

    public IReadOnlyList<BackgroundJob> All => _jobs;

    public BackgroundJob Enqueue(
        string payload,
        string kind = BackgroundJobKinds.YouTubeSummary,
        long updateId = 1,
        long chatId = 42,
        long senderId = 7)
    {
        var job = new BackgroundJob(
            _nextId++,
            updateId,
            kind,
            payload,
            chatId,
            senderId,
            BackgroundJobStatus.Pending,
            Attempts: 0,
            LastError: null,
            EnqueuedAt: DateTimeOffset.UnixEpoch,
            NextAttemptAt: DateTimeOffset.UnixEpoch);

        _jobs.Add(job);
        return job;
    }

    public BackgroundJob this[long id] => _jobs.Single(j => j.Id == id);

    public Task<BackgroundJob?> GetNextPendingAsync(string kind, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        Task.FromResult(_jobs
            .Where(j => j.Kind == kind && j.Status == BackgroundJobStatus.Pending && j.NextAttemptAt <= now)
            .OrderBy(j => j.Id)
            .FirstOrDefault());

    public Task RecordFailureAsync(long jobId, string error, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken = default)
    {
        Replace(jobId, job => job with { Attempts = job.Attempts + 1, LastError = error, NextAttemptAt = nextAttemptAt });
        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(long jobId, string error, CancellationToken cancellationToken = default)
    {
        Replace(jobId, job => job with { Attempts = job.Attempts + 1, LastError = error, Status = BackgroundJobStatus.Failed });
        return Task.CompletedTask;
    }

    /// <summary>Completion runs through the unit of work, not this queue; tests call it to mirror that.</summary>
    public void Complete(long jobId) => Replace(jobId, job => job with { Status = BackgroundJobStatus.Completed });

    private void Replace(long jobId, Func<BackgroundJob, BackgroundJob> update)
    {
        var index = _jobs.FindIndex(j => j.Id == jobId);
        if (index >= 0)
        {
            _jobs[index] = update(_jobs[index]);
        }
    }
}
