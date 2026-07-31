using MindBot.Core.Durability;
using MindBot.Core.Operations;
using MindBot.Core.Options;
using Microsoft.Extensions.Options;

namespace MindBot.Core.Commands;

/// <summary>
/// Dispatches a message to its matching <see cref="ICommand"/> and, for an
/// <see cref="CommandResult.Operations"/> result, resolves every operation before enqueueing any
/// of them.
/// <para>
/// Resolving fully before enqueueing is what stands in for the "roll back on partial failure"
/// requirement: nothing is enqueued until every operation has resolved successfully, and nothing
/// enqueued yet means the enclosing SQLite ingest transaction (<see cref="IIngestUnitOfWork"/>)
/// rolls back cleanly on any unhandled exception without git or the filesystem ever having been
/// touched -- a stronger guarantee than a git-level cleanup would give.
/// </para>
/// </summary>
public sealed class CommandExecutor(
    CommandDispatcher dispatcher,
    VaultOperationApplier operationApplier,
    IOptions<VaultOptions> vaultOptions)
{
    public async Task<string> ExecuteAsync(
        IIngestUnitOfWork unitOfWork,
        long updateId,
        long chatId,
        long senderId,
        string messageText,
        CancellationToken cancellationToken = default)
    {
        var command = dispatcher.Match(messageText);
        var context = new UnitOfWorkVaultOperationContext(unitOfWork, vaultOptions.Value.Root);
        var result = await command.HandleAsync(messageText, context, cancellationToken);

        switch (result)
        {
            case CommandResult.DirectReply direct:
                return direct.Text;

            case CommandResult.Rejected rejected:
                return $"Can't do that: {rejected.Reason}";

            case CommandResult.Operations ops:
                var resolved = new List<ResolvedWrite>(ops.Items.Count);
                try
                {
                    foreach (var operation in ops.Items)
                    {
                        resolved.Add(await operationApplier.ResolveAsync(operation, context, cancellationToken));
                    }
                }
                catch (VaultOperationException ex)
                {
                    return $"Could not complete that: {ex.Message}";
                }

                foreach (var write in resolved)
                {
                    await unitOfWork.EnqueueWriteJobAsync(updateId, write.RelativeFolder, write.Filename, write.Content, chatId, senderId, cancellationToken);
                }

                return ops.Reply;

            default:
                throw new InvalidOperationException($"Unhandled {nameof(CommandResult)} type {result.GetType().Name}.");
        }
    }
}
