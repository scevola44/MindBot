using System.Text;
using MindBot.Core.Operations;
using MindBot.Core.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MindBot.Core.Commands;

/// <summary>
/// Parses the inner command text, resolves its operations against a throwaway scratch copy of the
/// target file(s), and replies with the resulting content. Never touches the real vault, the
/// write-job queue, or git.
/// <para>
/// Neither <see cref="CommandResult.Operations"/> nor <see cref="CommandResult.DeferredJob"/> can
/// escape this command -- both are always converted to <see cref="CommandResult.DirectReply"/> or
/// <see cref="CommandResult.Rejected"/> before returning, so <see cref="CommandExecutor"/>
/// structurally never reaches either of its enqueue branches for a /preview invocation. Since
/// <see cref="ScratchVaultOperationContext"/> and every operation
/// handler only ever reference <see cref="IVaultOperationContext"/> (never IGitService or
/// IIngestUnitOfWork), zero git calls and zero queue writes are structural guarantees here, not
/// just untested code paths.
/// </para>
/// </summary>
public sealed class PreviewCommand(IServiceProvider serviceProvider, VaultOperationApplier operationApplier, IOptions<VaultOptions> vaultOptions) : ICommand
{
    public bool Matches(string messageText) => string.Equals(CommandText.ExtractCommand(messageText), "/preview", StringComparison.OrdinalIgnoreCase);

    public async Task<CommandResult> HandleAsync(string messageText, IVaultOperationContext context, CancellationToken cancellationToken = default)
    {
        var inner = CommandText.ExtractArgument(messageText);
        if (string.IsNullOrWhiteSpace(inner))
        {
            return new CommandResult.Rejected("Usage: /preview <command...>");
        }

        // Resolved lazily via IServiceProvider, not constructor-injected: PreviewCommand is itself
        // one of the ICommands CommandDispatcher aggregates, so constructor injection here would be
        // a circular DI dependency. By the time HandleAsync runs the container has finished
        // building the whole graph, so this is safe.
        var dispatcher = serviceProvider.GetRequiredService<CommandDispatcher>();
        var innerCommand = dispatcher.Match(inner);
        if (innerCommand is PreviewCommand)
        {
            return new CommandResult.Rejected("/preview cannot preview itself.");
        }

        var scratch = new ScratchVaultOperationContext(vaultOptions.Value.Root);
        var innerResult = await innerCommand.HandleAsync(inner, scratch, cancellationToken);

        return innerResult switch
        {
            CommandResult.DirectReply direct => new CommandResult.DirectReply($"[preview] {direct.Text}"),
            CommandResult.Rejected rejected => rejected,
            // Reported, never enqueued -- and since the inner command only *describes* the job
            // rather than running it, previewing /ytsummary makes zero network calls either.
            CommandResult.DeferredJob deferred => new CommandResult.DirectReply(
                $"[preview] would queue a '{deferred.Kind}' job: {deferred.Payload}"),
            CommandResult.Operations ops => await PreviewOperationsAsync(ops, scratch, cancellationToken),
            _ => throw new InvalidOperationException($"Unhandled {nameof(CommandResult)} type {innerResult.GetType().Name}."),
        };
    }

    private async Task<CommandResult> PreviewOperationsAsync(CommandResult.Operations ops, ScratchVaultOperationContext scratch, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        try
        {
            foreach (var operation in ops.Items)
            {
                var resolved = await operationApplier.ResolveAsync(operation, scratch, cancellationToken);
                scratch.RecordResolved(resolved);
                builder.AppendLine($"--- {Path.Combine(resolved.RelativeFolder, resolved.Filename)} ---");
                builder.Append(resolved.Content);
            }
        }
        catch (VaultOperationException ex)
        {
            return new CommandResult.Rejected($"Preview failed: {ex.Message}");
        }

        return new CommandResult.DirectReply(builder.ToString());
    }
}
