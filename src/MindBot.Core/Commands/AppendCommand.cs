using MindBot.Core.Operations;

namespace MindBot.Core.Commands;

/// <summary>
/// A deliberate placeholder exercising <see cref="AppendToNote"/> end to end against a single
/// fixed note. Expected to be deleted (not extended) once real commands like a future /task
/// successor exist.
/// </summary>
public sealed class AppendCommand : ICommand
{
    private const string ScratchPath = "Scratch.md";

    public bool Matches(string messageText) => string.Equals(CommandText.ExtractCommand(messageText), "/append", StringComparison.OrdinalIgnoreCase);

    public Task<CommandResult> HandleAsync(string messageText, IVaultOperationContext context, CancellationToken cancellationToken = default)
    {
        var text = CommandText.ExtractArgument(messageText);
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult<CommandResult>(new CommandResult.Rejected("Usage: /append <text>"));
        }

        var operation = new AppendToNote(ScratchPath, text);
        return Task.FromResult<CommandResult>(new CommandResult.Operations([operation], $"Append to {ScratchPath}", $"Appended to {ScratchPath}."));
    }
}
