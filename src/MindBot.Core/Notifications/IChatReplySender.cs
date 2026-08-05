namespace MindBot.Core.Notifications;

/// <summary>
/// Sends a message back to a specific chat. Distinct from <see cref="IOperatorNotifier"/>, which
/// always targets the configured operator: deferred work has to answer whoever asked for it, and
/// the chat id is carried on the job rather than read from configuration.
/// <para>
/// Kept as a Core abstraction so the background-job runner needs no dependency on Telegram.Bot.
/// Implementations must swallow delivery failures — a lost confirmation costs a message, not the
/// note.
/// </para>
/// </summary>
public interface IChatReplySender
{
    Task SendAsync(long chatId, string text, CancellationToken cancellationToken = default);
}
