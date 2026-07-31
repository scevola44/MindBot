namespace MindBot.Bot.Services;

/// <summary>
/// Wakes the drain worker when the ingest loop accepts a job, so the worker can sit on a long
/// wait instead of polling SQLite in a spin loop.
/// <para>
/// Over-signalling is harmless — the extra wake-up finds an empty queue and goes back to waiting —
/// so the count is deliberately capped at one rather than tracking an exact backlog.
/// </para>
/// </summary>
public sealed class WriteJobSignal
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
