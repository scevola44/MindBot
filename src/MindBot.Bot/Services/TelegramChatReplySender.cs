using MindBot.Core.Notifications;
using Telegram.Bot;

namespace MindBot.Bot.Services;

/// <summary>
/// Delivers a background job's answer to the chat that asked for it.
/// <para>
/// Swallows delivery failures by design, exactly as the poller's own reply path does: by the time
/// this runs the note is already durably queued, and an unreachable Telegram must not turn a
/// finished capture into a retried one.
/// </para>
/// </summary>
public sealed class TelegramChatReplySender(ITelegramBotClient botClient, ILogger<TelegramChatReplySender> logger) : IChatReplySender
{
    public async Task SendAsync(long chatId, string text, CancellationToken cancellationToken = default)
    {
        try
        {
            await botClient.SendMessage(chatId, text, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not send a background-job reply to chat {ChatId}.", chatId);
        }
    }
}
