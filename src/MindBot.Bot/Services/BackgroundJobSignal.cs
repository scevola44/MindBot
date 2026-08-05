namespace MindBot.Bot.Services;

/// <summary>
/// Wakes the background-job worker when the ingest loop accepts a deferred job, so a /ytsummary
/// starts immediately instead of waiting out the worker's idle poll.
/// <para>
/// Deliberately a separate signal from <see cref="WriteJobSignal"/>: the two workers run at wildly
/// different cadences (one commits in milliseconds, the other runs for minutes), and sharing a
/// signal would make every note write wake the pipeline worker for nothing.
/// </para>
/// </summary>
public sealed class BackgroundJobSignal
{
    private readonly SemaphoreSlim _semaphore = new(0, 1);

    public void Signal()
    {
        try
        {
            _semaphore.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already signalled and not yet consumed; the pending wake-up covers this job too.
        }
    }

    /// <summary>Returns true if a job was signalled, false if the wait timed out.</summary>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _semaphore.WaitAsync(timeout, cancellationToken);
}
