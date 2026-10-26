using MindBot.Core.Durability;
using MindBot.Core.Notifications;
using MindBot.Core.Web;

namespace MindBot.Bot.Web;

/// <summary>
/// Delivers a deferred reply (a /ytsummary result) to the web chat: store it, then push it to any
/// connected device. Storing first is what makes it survive the device being offline.
/// </summary>
public sealed class WebChatReplySender(IWebTranscript transcript, WebMessageHub hub, ILogger<WebChatReplySender> logger) : IChannelReplySender
{
    public Channel Channel => Channel.Web;

    public async Task SendAsync(long chatId, string text, CancellationToken cancellationToken = default)
    {
        try
        {
            await transcript.AppendAsync(WebMessageDirection.Out, text, cancellationToken);
            hub.NotifyNewMessages();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Same contract as the Telegram sender: the note is already durable, only the
            // confirmation is lost. Not logged with its text, which can quote vault content.
            logger.LogError(ex, "Could not store a deferred reply for the web chat.");
        }
    }
}

/// <summary>Posts operator alerts into the web chat when Telegram has no operator chat to send them to.</summary>
public sealed class WebOperatorAlertSink(IWebTranscript transcript, WebMessageHub hub, ILogger<WebOperatorAlertSink> logger) : IOperatorAlertSink
{
    public async Task SendAsync(string message, CancellationToken cancellationToken = default)
    {
        try
        {
            await transcript.AppendAsync(WebMessageDirection.System, message, cancellationToken);
            hub.NotifyNewMessages();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not deliver an operator alert to the web chat. The alert was: {Message}", message);
        }
    }
}
