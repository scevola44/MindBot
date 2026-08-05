using MindBot.Core.Commands;
using MindBot.Core.Durability;
using MindBot.Core.Notes;
using MindBot.Core.Notifications;
using MindBot.Core.Operations;
using MindBot.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MindBot.Core.YouTube;

/// <summary>What one pass of <see cref="YouTubeSummaryJobRunner.RunNextAsync"/> did.</summary>
public enum BackgroundJobOutcome
{
    /// <summary>Nothing was claimable — either the queue is empty or every job is still backing off.</summary>
    Idle,

    /// <summary>A note is durably queued and the job is closed.</summary>
    Completed,

    /// <summary>The attempt failed and the job is scheduled for another one.</summary>
    Retrying,

    /// <summary>The job is out of attempts (or was unrunnable) and will never be claimed again.</summary>
    Failed,
}

/// <summary>
/// Runs one queued /ytsummary job to completion: the n8n pipeline, then the note.
/// <para>
/// The whole point of this type is that the slow part happens <em>outside</em> any transaction.
/// Only once the summary exists is a unit of work opened, and it does three fast things — reserve
/// the filename, queue the note, close the job — before committing. That single commit is what
/// makes a crash safe in both directions: before it, the job is still pending and the pipeline
/// re-runs; after it, the job is closed and the note is queued exactly once.
/// </para>
/// </summary>
public sealed class YouTubeSummaryJobRunner(
    IBackgroundJobQueue queue,
    YouTubeSummaryPipeline pipeline,
    IIngestUnitOfWorkFactory unitOfWorkFactory,
    VaultOperationApplier operationApplier,
    IChatReplySender replySender,
    IOptions<VaultOptions> vaultOptions,
    IOptions<N8nOptions> n8nOptions,
    TimeProvider timeProvider,
    ILogger<YouTubeSummaryJobRunner> logger)
{
    public async Task<BackgroundJobOutcome> RunNextAsync(CancellationToken cancellationToken = default)
    {
        var job = await queue.GetNextPendingAsync(BackgroundJobKinds.YouTubeSummary, timeProvider.GetUtcNow(), cancellationToken);
        if (job is null)
        {
            return BackgroundJobOutcome.Idle;
        }

        YouTubeSummaryPayload payload;
        try
        {
            payload = YouTubeSummaryCommand.ParsePayload(job.Payload);
        }
        catch (Exception ex)
        {
            // A payload this runner cannot read will never become readable, so retrying is pointless.
            logger.LogError(ex, "Background job {JobId} has an unreadable payload; abandoning it.", job.Id);
            await AbandonAsync(job, "the request could not be read", cancellationToken);
            return BackgroundJobOutcome.Failed;
        }

        YouTubeSummaryResult summary;
        try
        {
            summary = await pipeline.RunAsync(payload.VideoId, payload.ChunkCount, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down mid-pipeline: leave the job pending so the next start picks it up.
            throw;
        }
        catch (Exception ex)
        {
            return await HandleFailureAsync(job, ex, "the summarisation pipeline", cancellationToken);
        }

        string filename;
        try
        {
            filename = await QueueNoteAsync(job, summary, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The summary is lost with the transaction, so the retry re-runs the pipeline. That
            // costs LLM time but keeps the "note queued and job closed, or neither" guarantee.
            return await HandleFailureAsync(job, ex, "queueing the note", cancellationToken);
        }

        logger.LogInformation("Summarised {VideoId} into {Filename}.", payload.VideoId, filename);
        await replySender.SendAsync(job.ChatId, $"Summarised \"{summary.Title}\" → {filename}", cancellationToken);

        return BackgroundJobOutcome.Completed;
    }

    private async Task<string> QueueNoteAsync(BackgroundJob job, YouTubeSummaryResult summary, CancellationToken cancellationToken)
    {
        var note = YouTubeNoteBuilder.Build(summary, timeProvider.GetLocalNow());

        await using var unitOfWork = await unitOfWorkFactory.BeginAsync(cancellationToken);
        var context = new UnitOfWorkVaultOperationContext(unitOfWork, vaultOptions.Value.Root);

        var filename = await context.ReserveFilenameAsync(note.BaseFilename, cancellationToken);
        var operation = new CreateNote(VaultLayout.RelativeNotePath(filename), note.Frontmatter, note.Body);

        // Resolved through the applier rather than serialized here, so YAML emission stays solely
        // in CreateNoteHandler.
        var write = await operationApplier.ResolveAsync(operation, context, cancellationToken);

        await unitOfWork.EnqueueWriteJobAsync(
            job.UpdateId,
            write.RelativeFolder,
            write.Filename,
            write.Content,
            job.ChatId,
            job.SenderId,
            cancellationToken);

        await unitOfWork.CompleteBackgroundJobAsync(job.Id, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        return filename;
    }

    private async Task<BackgroundJobOutcome> HandleFailureAsync(
        BackgroundJob job,
        Exception exception,
        string stage,
        CancellationToken cancellationToken)
    {
        var attempts = job.Attempts + 1;
        var maxAttempts = n8nOptions.Value.MaxAttempts;

        if (attempts >= maxAttempts)
        {
            logger.LogError(
                exception,
                "Background job {JobId} failed at {Stage} on attempt {Attempts} of {MaxAttempts}; giving up.",
                job.Id,
                stage,
                attempts,
                maxAttempts);

            await AbandonAsync(job, $"{stage} kept failing: {exception.Message}", cancellationToken);
            return BackgroundJobOutcome.Failed;
        }

        var delay = TimeSpan.FromSeconds(n8nOptions.Value.RetryBaseSeconds * Math.Pow(2, attempts - 1));
        logger.LogWarning(
            exception,
            "Background job {JobId} failed at {Stage} on attempt {Attempts} of {MaxAttempts}; retrying in {Delay}.",
            job.Id,
            stage,
            attempts,
            maxAttempts,
            delay);

        await queue.RecordFailureAsync(job.Id, exception.Message, timeProvider.GetUtcNow() + delay, cancellationToken);
        return BackgroundJobOutcome.Retrying;
    }

    private async Task AbandonAsync(BackgroundJob job, string reason, CancellationToken cancellationToken)
    {
        await queue.MarkFailedAsync(job.Id, reason, cancellationToken);
        await replySender.SendAsync(job.ChatId, $"Could not summarise that video — {reason}", cancellationToken);
    }
}
