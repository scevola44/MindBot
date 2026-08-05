using MindBot.Core.Options;
using MindBot.Core.YouTube;
using Microsoft.Extensions.Options;

namespace MindBot.Bot.Services;

/// <summary>
/// Owns the timing around <see cref="YouTubeSummaryJobRunner"/>: wakes on a newly accepted job,
/// otherwise re-checks periodically so a job left backing off (or stranded by a restart) is picked
/// up without anyone sending another message.
/// <para>
/// Unlike <see cref="VaultSyncHostedService"/>, this deliberately does <em>not</em> drain in
/// StartAsync. One job can take minutes, and blocking host startup on a backlog of them would keep
/// the poller — and every fast capture — offline for that whole time. The leftovers are picked up
/// by the first loop iteration instead.
/// </para>
/// </summary>
public sealed class BackgroundJobHostedService(
    YouTubeSummaryJobRunner runner,
    BackgroundJobSignal signal,
    WriteJobSignal writeJobSignal,
    IOptions<N8nOptions> n8nOptions,
    ILogger<BackgroundJobHostedService> logger) : BackgroundService
{
    /// <summary>How long the worker waits before re-checking for a job whose retry backoff has elapsed.</summary>
    private static readonly TimeSpan IdleRecheckInterval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!n8nOptions.Value.IsConfigured)
        {
            logger.LogInformation("N8N__BASEURL is not set; the background summary worker is disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainUntilIdleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The runner already converts job failures into retries; reaching here means
                // something outside a single job broke. The queue is durable, so wait and retry.
                logger.LogError(ex, "Background job worker cycle failed; retrying shortly.");
                if (!await SafeDelayAsync(TimeSpan.FromSeconds(5), stoppingToken))
                {
                    break;
                }
            }

            try
            {
                await signal.WaitAsync(IdleRecheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Runs jobs one at a time until nothing is claimable. Each job is independent, so a failed one
    /// does not stop the next: only <see cref="BackgroundJobOutcome.Idle"/> ends the pass.
    /// </summary>
    private async Task DrainUntilIdleAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var outcome = await runner.RunNextAsync(cancellationToken);

            if (outcome == BackgroundJobOutcome.Completed)
            {
                // The note is queued but nothing else knows yet; without this it would sit until
                // the sync worker's next idle refresh.
                writeJobSignal.Signal();
            }

            if (outcome == BackgroundJobOutcome.Idle)
            {
                return;
            }
        }
    }

    /// <summary>Returns false when the delay was cut short by shutdown.</summary>
    private static async Task<bool> SafeDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
