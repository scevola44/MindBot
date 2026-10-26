using MindBot.Bot.Services;
using MindBot.Core.Durability;
using MindBot.Core.Ingest;
using MindBot.Core.Web;

namespace MindBot.Bot.Web;

public sealed record WebAcceptResult(WebMessage Inbound, WebMessage Reply, bool WasDuplicate);

/// <summary>
/// Accepts one message from a device. The web counterpart of TelegramPollingHostedService's
/// HandleUpdateAsync, with the same rule: everything happens in one SQLite transaction and
/// nothing slow happens inside it.
/// </summary>
public sealed class WebMessageService(
    IWebIngestUnitOfWorkFactory unitOfWorkFactory,
    MessageRouter messageRouter,
    WriteJobSignal writeJobSignal,
    BackgroundJobSignal backgroundJobSignal,
    WebMessageHub hub,
    ILogger<WebMessageService> logger)
{
    public async Task<WebAcceptResult> AcceptAsync(long deviceId, string clientMessageId, string text, CancellationToken cancellationToken = default)
    {
        WebMessage inbound;
        WebMessage reply;

        await using (var unitOfWork = await unitOfWorkFactory.BeginWebAsync(cancellationToken))
        {
            var existing = await unitOfWork.FindInboundAsync(clientMessageId, cancellationToken);
            if (existing is not null)
            {
                // A retry: the device never saw our answer. Hand back the stored one; queue nothing.
                var storedReply = await unitOfWork.FindReplyAsync(existing.Id, cancellationToken)
                    ?? throw new InvalidOperationException($"Web message {existing.Id} was accepted without a reply.");

                logger.LogInformation("Web message {MessageId} was already accepted; returning its stored reply.", existing.Id);
                return new WebAcceptResult(existing, storedReply, WasDuplicate: true);
            }

            inbound = await unitOfWork.AppendAsync(WebMessageDirection.In, text, clientMessageId, cancellationToken: cancellationToken);

            // The transcript id is the Web channel's update id: assigned once, never reused.
            var replyText = await messageRouter.RouteAsync(unitOfWork, inbound.Id, WebChat.Address, deviceId, text, cancellationToken);

            reply = await unitOfWork.AppendAsync(WebMessageDirection.Out, replyText, inReplyToId: inbound.Id, cancellationToken: cancellationToken);
            await unitOfWork.MarkUpdateProcessedAsync(Channel.Web, inbound.Id, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }

        writeJobSignal.Signal();
        backgroundJobSignal.Signal();

        // Other devices see both lines live; the sender also gets them in the HTTP response.
        hub.NotifyNewMessages();

        return new WebAcceptResult(inbound, reply, WasDuplicate: false);
    }
}
