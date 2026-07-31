namespace MindBot.Core.Commands;

/// <summary>
/// Picks the first matching <see cref="ICommand"/>. No hardcoded switch/if-chain over command
/// text: this only ever calls <see cref="ICommand.Matches"/>.
/// <para>
/// <see cref="BareTextCommand"/> is registered last -- it matches unconditionally, acting as the
/// catch-all fallback for plain text. The .NET DI container resolves <c>IEnumerable&lt;T&gt;</c>
/// in registration order, which this relies on.
/// </para>
/// </summary>
public sealed class CommandDispatcher(IEnumerable<ICommand> commands)
{
    public ICommand Match(string messageText) => commands.First(c => c.Matches(messageText));
}
