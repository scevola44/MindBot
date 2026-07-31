using MindBot.Core.Durability;
using MindBot.Core.Git;
using MindBot.Core.Health;
using MindBot.Core.Notes;
using MindBot.Core.Notifications;
using MindBot.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MindBot.Core.Sync;

public enum DrainResult
{
    /// <summary>Nothing was queued.</summary>
    Idle,

    /// <summary>The batch was committed and pushed.</summary>
    Pushed,

    /// <summary>The batch was committed locally but could not be pushed; the bot is degraded.</summary>
    CommittedNotPushed,

    /// <summary>The batch could not be committed. The notes are on disk and will be picked up again.</summary>
    CommitFailed,
}

/// <summary>
/// Drains the durable write-job queue into the vault repository: classify, pull, write the whole
/// batch, make <em>one</em> commit, push with bounded retry.
/// <para>
/// Jobs are marked complete once the commit succeeds, not once the push does. The commit is the
/// durability boundary — at that point the note is in git history and cannot be lost — while an
/// unpushed commit is a degraded state tracked separately and retried on every later cycle. Tying
/// completion to the push instead would let the queue grow without bound whenever the remote is
/// down, and would re-write the same notes on every retry.
/// </para>
/// </summary>
public sealed class VaultSyncService(
    IGitService gitService,
    IVaultWriter vaultWriter,
    IWriteJobQueue jobQueue,
    IRepositoryStateStore repositoryState,
    IOperatorNotifier operatorNotifier,
    HealthSnapshot health,
    TimeProvider timeProvider,
    IOptions<GitOptions> gitOptions,
    ILogger<VaultSyncService> logger)
{
    private readonly GitOptions _gitOptions = gitOptions.Value;

    /// <summary>
    /// Processes every currently-queued job as a single commit. Returns <see cref="DrainResult.Idle"/>
    /// when the queue is empty, having refreshed the health snapshot.
    /// </summary>
    public async Task<DrainResult> DrainOnceAsync(CancellationToken cancellationToken = default)
    {
        var jobs = await jobQueue.GetPendingAsync(_gitOptions.MaxBatchSize, cancellationToken);
        if (jobs.Count == 0)
        {
            // Status only. Retrying a stuck push belongs to the idle path, so a drain loop that
            // has just finished does not pay the push-backoff cost a second time.
            await RefreshStatusAsync(cancellationToken);
            return DrainResult.Idle;
        }

        health.RecordQueueDepth(await jobQueue.GetPendingCountAsync(cancellationToken));

        var state = await repositoryState.GetAsync(cancellationToken);
        await SynchronizeAsync(state.LastPushedSha, cancellationToken);

        await WriteBatchAsync(jobs, cancellationToken);

        var commitResult = await gitService.CommitAsync(BuildCommitMessage(jobs), cancellationToken);
        if (!commitResult.Success)
        {
            // The notes are already on disk. Leaving the jobs pending means the next cycle
            // rewrites them (idempotently) and the clean-tree recovery commits them, so nothing
            // is lost by failing here.
            logger.LogError("Git commit failed for a batch of {Count} note(s). {Error}", jobs.Count, commitResult.ErrorMessage);
            return DrainResult.CommitFailed;
        }

        await jobQueue.MarkCompletedAsync(jobs.Select(job => job.Id).ToArray(), cancellationToken);
        health.RecordQueueDepth(await jobQueue.GetPendingCountAsync(cancellationToken));

        var pushed = await PushWithRetryAsync(jobs, cancellationToken);
        await RefreshStatusAsync(cancellationToken);

        return pushed ? DrainResult.Pushed : DrainResult.CommittedNotPushed;
    }

    /// <summary>
    /// Refreshes the cached git status and, if the bot is holding un-pushed commits, makes one
    /// attempt to push them. Called on the idle path so a remote that recovers during a quiet
    /// period does not leave the bot degraded until the next message arrives.
    /// </summary>
    public async Task RefreshAndRetryPushAsync(CancellationToken cancellationToken = default)
    {
        var status = await RefreshStatusAsync(cancellationToken);
        if (status.UnpushedCommitCount == 0)
        {
            return;
        }

        logger.LogInformation(
            "Holding {Count} un-pushed commit(s); retrying the push while the queue is idle.",
            status.UnpushedCommitCount);

        if (await PushWithRetryAsync(jobs: null, cancellationToken))
        {
            await RefreshStatusAsync(cancellationToken);
        }
    }

    public async Task<GitStatusSnapshot> RefreshStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = await gitService.GetStatusAsync(cancellationToken);
        health.RecordGitStatus(status);
        health.RecordQueueDepth(await jobQueue.GetPendingCountAsync(cancellationToken));
        return status;
    }

    private async Task<GitClassification> SynchronizeAsync(string? lastPushedSha, CancellationToken cancellationToken)
    {
        var classification = await gitService.SynchronizeAsync(lastPushedSha, cancellationToken);
        health.RecordClassification(classification.Strategy);

        if (classification.WorkingTreeWasDirty)
        {
            logger.LogWarning("Working tree was dirty on entry; uncommitted note content was committed before synchronising.");
        }

        switch (classification.Strategy)
        {
            case GitSyncStrategy.RemoteUnreachable:
                logger.LogWarning(
                    "Could not reach the remote; proceeding with a local-only write. {Error}",
                    classification.Error);
                break;

            case GitSyncStrategy.OperatorAdvanced:
                logger.LogInformation(
                    "Operator advanced the branch; rebased {Count} un-pushed commit(s) onto it.",
                    classification.UnpushedCommitCount);
                break;

            case GitSyncStrategy.RemoteRewritten when classification.RecoveryBundlePath is not null:
                logger.LogWarning(
                    "Branch was reset or rewritten after triage. Did not rebase — {Count} un-pushed commit(s) were exported to {BundlePath} and the branch was reset to origin.",
                    classification.RecoveredCommitCount,
                    classification.RecoveryBundlePath);
                await operatorNotifier.NotifyAsync(
                    $"⚠️ MindBot: the branch '{_gitOptions.Branch}' was reset or rewritten while I held " +
                    $"{classification.RecoveredCommitCount} un-pushed commit(s).\n\n" +
                    "I did not rebase — that would have resurrected notes you already triaged. " +
                    $"Those commits are saved in:\n{classification.RecoveryBundlePath}\n\n" +
                    "To inspect them:\n" +
                    $"git fetch {classification.RecoveryBundlePath} HEAD:recovered-notes && git log recovered-notes",
                    cancellationToken);
                break;

            case GitSyncStrategy.RemoteRewritten:
                // The bundle could not be written, so nothing was discarded and the bot stays degraded.
                logger.LogError(
                    "Branch was reset or rewritten, but the recovery bundle could not be written; refusing to discard {Count} un-pushed commit(s). {Error}",
                    classification.UnpushedCommitCount,
                    classification.Error);
                await operatorNotifier.NotifyAsync(
                    $"⚠️ MindBot: the branch '{_gitOptions.Branch}' was rewritten, but I could not write a recovery bundle, " +
                    $"so I have kept my {classification.UnpushedCommitCount} un-pushed commit(s) and will not push. " +
                    $"Manual intervention needed. Error: {classification.Error}",
                    cancellationToken);
                break;
        }

        return classification;
    }

    private async Task WriteBatchAsync(IReadOnlyList<WriteJob> jobs, CancellationToken cancellationToken)
    {
        foreach (var job in jobs)
        {
            await vaultWriter.WriteNoteAsync(job.RelativeFolder, job.Filename, job.Content, cancellationToken);
        }
    }

    /// <summary>
    /// Pushes, re-classifying and re-applying the batch on a non-fast-forward rejection and
    /// backing off exponentially on a network failure.
    /// </summary>
    /// <param name="jobs">
    /// The batch to re-apply if a re-classification rewinds the branch, or null when retrying a
    /// push that has no batch behind it (the idle path).
    /// </param>
    private async Task<bool> PushWithRetryAsync(IReadOnlyList<WriteJob>? jobs, CancellationToken cancellationToken)
    {
        GitPushResult? lastResult = null;

        for (var attempt = 1; attempt <= _gitOptions.PushRetryCount; attempt++)
        {
            var result = await gitService.PushAsync(cancellationToken);
            if (result.Success)
            {
                await RecordSuccessfulPushAsync(cancellationToken);
                await operatorNotifier.ClearAsync(OperatorAlertKeys.PushFailing, cancellationToken);
                return true;
            }

            lastResult = result;

            if (attempt == _gitOptions.PushRetryCount)
            {
                break;
            }

            if (result.Failure == GitPushFailure.Rejected)
            {
                logger.LogWarning(
                    "Push rejected as non-fast-forward (attempt {Attempt}/{Total}); re-classifying against the remote. {Error}",
                    attempt,
                    _gitOptions.PushRetryCount,
                    result.ErrorMessage);

                var state = await repositoryState.GetAsync(cancellationToken);
                var classification = await SynchronizeAsync(state.LastPushedSha, cancellationToken);

                // A RemoteRewritten classification resets the branch, discarding the commit this
                // batch just made. Those notes were captured seconds ago and have never been
                // pushed, so they cannot be ones the operator triaged — re-apply them on top of
                // the new origin rather than leaving them only in the bundle. For every other
                // strategy the files are already present and both calls are no-ops.
                if (jobs is { Count: > 0 } && classification.Strategy == GitSyncStrategy.RemoteRewritten)
                {
                    await WriteBatchAsync(jobs, cancellationToken);
                    var recommit = await gitService.CommitAsync(BuildCommitMessage(jobs), cancellationToken);
                    if (!recommit.Success)
                    {
                        logger.LogError("Could not re-commit the batch after the branch was reset. {Error}", recommit.ErrorMessage);
                    }
                }
            }
            else
            {
                logger.LogWarning(
                    "Push failed for network reasons (attempt {Attempt}/{Total}). {Error}",
                    attempt,
                    _gitOptions.PushRetryCount,
                    result.ErrorMessage);
            }

            var delay = TimeSpan.FromSeconds(_gitOptions.PushRetryBaseSeconds * Math.Pow(2, attempt - 1));
            await Task.Delay(delay, timeProvider, cancellationToken);
        }

        logger.LogWarning(
            "Push did not succeed after {Attempts} attempt(s); notes are committed locally only. {Error}",
            _gitOptions.PushRetryCount,
            lastResult?.ErrorMessage);

        await operatorNotifier.NotifyOnceAsync(
            OperatorAlertKeys.PushFailing,
            $"⚠️ MindBot: I have not been able to push to '{_gitOptions.Branch}' after " +
            $"{_gitOptions.PushRetryCount} attempts. Notes are still being captured and committed locally, " +
            "so nothing is being lost, but they are not reaching the remote. " +
            $"Last error: {lastResult?.ErrorMessage}",
            cancellationToken);

        return false;
    }

    private async Task RecordSuccessfulPushAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        health.RecordSuccessfulPush(now);

        var sha = await gitService.GetHeadShaAsync(cancellationToken);
        if (sha is null)
        {
            logger.LogWarning("Pushed successfully but could not read HEAD; lastPushedSha not updated.");
            return;
        }

        await repositoryState.SetLastPushedShaAsync(sha, now, cancellationToken);
    }

    private static string BuildCommitMessage(IReadOnlyList<WriteJob> jobs) =>
        jobs.Count == 1 ? $"Add note {jobs[0].Filename}" : $"Add {jobs.Count} notes";
}
