using MindBot.Core.Git;
using Microsoft.Extensions.Logging;

namespace MindBot.Core.Notes;

/// <summary>
/// Orchestrates turning a message into a committed note: pull, write, commit, push.
/// A failed pull or push is logged and does not abort the write — a message must
/// never be lost because the remote is unreachable.
/// </summary>
public sealed class NotePipeline(
    IGitService gitService,
    IVaultWriter vaultWriter,
    TimeProvider timeProvider,
    ILogger<NotePipeline> logger)
{
    public async Task<NoteCreationResult> CreateNoteAsync(string messageText, CancellationToken cancellationToken = default)
    {
        var created = timeProvider.GetLocalNow();
        var filename = NoteFilenameFactory.Create(created, messageText);
        var content = NoteContentBuilder.Build(messageText, created);

        var pullResult = await gitService.PullAsync(cancellationToken);
        if (!pullResult.Success)
        {
            logger.LogWarning("Git pull failed; continuing with a local-only write. {Error}", pullResult.ErrorMessage);
        }

        await vaultWriter.WriteNoteAsync(filename, content, cancellationToken);

        var commitResult = await gitService.CommitAsync($"Add note {filename}", cancellationToken);
        if (!commitResult.Success)
        {
            logger.LogError("Git commit failed for {Filename}. {Error}", filename, commitResult.ErrorMessage);
        }

        var pushResult = await gitService.PushAsync(cancellationToken);
        if (!pushResult.Success)
        {
            logger.LogWarning("Git push failed; note {Filename} is committed locally only. {Error}", filename, pushResult.ErrorMessage);
        }

        return new NoteCreationResult(filename);
    }
}
