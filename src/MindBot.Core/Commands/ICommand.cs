using MindBot.Core.Operations;

namespace MindBot.Core.Commands;

/// <summary>
/// Discovered via DI (<c>IEnumerable&lt;ICommand&gt;</c>, see <see cref="CommandDispatcher"/>) --
/// adding a command requires only a new implementation of this interface plus a DI registration,
/// never a dispatcher edit.
/// <para>
/// "No filesystem/git access from this layer" means no raw File.*/git-subprocess calls and no
/// writes of any kind. Read-only lookups through <see cref="IVaultOperationContext"/> ARE
/// permitted and expected -- MessageRouter's existing /task handling already does exactly this
/// today via GetLatestNoteContentAsync before enqueueing.
/// </para>
/// </summary>
public interface ICommand
{
    bool Matches(string messageText);

    Task<CommandResult> HandleAsync(string messageText, IVaultOperationContext context, CancellationToken cancellationToken = default);
}
