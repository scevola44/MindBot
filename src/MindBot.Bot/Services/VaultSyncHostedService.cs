using MindBot.Core.Options;
using MindBot.Core.Sync;
using Microsoft.Extensions.Options;

namespace MindBot.Bot.Services;

/// <summary>
/// Owns the timing around <see cref="VaultSyncService"/>: drains the backlog at startup, batches
/// bursts into one commit, and keeps the health snapshot fresh while idle.
/// <para>
/// This is the single writer to the vault repository. Nothing else calls the sync service, which
/// is what lets the git layer assume its own operations do not interleave.
/// </para>
/// </summary>
public sealed class VaultSyncHostedService(
    VaultSyncService syncService,
    WriteJobSignal signal,
    TimeProvider timeProvider,
    IOptions<GitOptions> gitOptions,
    ILogger<VaultSyncHostedService> logger) : BackgroundService
{
    /// <summary>How long the worker sits idle before refreshing git status and retrying a stuck push.</summary>
    private static readonly TimeSpan IdleRefreshInterval = TimeSpan.FromSeconds(60);

    private readonly GitOptions _gitOptions = gitOptions.Value;

    /// <summary>
    /// Drains everything already queued before the host finishes starting, so the poller never
    /// begins accepting new updates on top of an unprocessed backlog from the previous run.
    /// </summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Draining any write jobs left over from the previous run...");
        await DrainUntilIdleAsync(cancellationToken);

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            bool signalled;
            try
            {
                signalled = await signal.WaitAsync(IdleRefreshInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                if (signalled)
                {
                    // Fixed window measured from the first job, not slid forward on each arrival:
                    // a sustained burst must still commit promptly rather than being deferred
                    // for as long as messages keep coming.
                    await Task.Delay(TimeSpan.FromSeconds(_gitOptions.BatchWindowSeconds), timeProvider, stoppingToken);
                    await DrainUntilIdleAsync(CancellationToken.None);
                }
                else
                {
                    await syncService.RefreshAndRetryPushAsync(CancellationToken.None);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Vault sync cycle failed; the queue is durable, so it will be retried.");
                await SafeDelayAsync(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    /// <summary>
    /// Runs drain cycles until the queue reports empty. One cycle only takes MaxBatchSize jobs, so
    /// a backlog larger than that needs several passes.
    /// </summary>
    private async Task DrainUntilIdleAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = await syncService.DrainOnceAsync(cancellationToken);

            if (result is DrainResult.Idle or DrainResult.CommitFailed)
            {
                // CommitFailed leaves the jobs pending on purpose; retrying immediately would spin.
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private static async Task SafeDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
