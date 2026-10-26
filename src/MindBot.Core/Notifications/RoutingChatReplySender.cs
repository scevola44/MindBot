using MindBot.Core.Durability;
using Microsoft.Extensions.Logging;

namespace MindBot.Core.Notifications;

/// <summary>
/// Delivers a reply through one specific channel. Each transport registers one; it never has to
/// check whether a reply is meant for it, because <see cref="RoutingChatReplySender"/> already did.
/// Implementations must swallow delivery failures, exactly as <see cref="IChatReplySender"/> requires.
/// </summary>
public interface IChannelReplySender
{
    Channel Channel { get; }

    Task SendAsync(long chatId, string text, CancellationToken cancellationToken = default);
}

/// <summary>
/// The <see cref="IChatReplySender"/> everything else depends on. It picks the sender registered
/// for the reply's channel, so deferred work (a /ytsummary result) answers on the channel it was
/// asked on and can never leak to another one.
/// </summary>
public sealed class RoutingChatReplySender : IChatReplySender
{
    private readonly IReadOnlyDictionary<Channel, IChannelReplySender> _senders;
    private readonly ILogger<RoutingChatReplySender> _logger;

    public RoutingChatReplySender(IEnumerable<IChannelReplySender> senders, ILogger<RoutingChatReplySender> logger)
    {
        var byChannel = new Dictionary<Channel, IChannelReplySender>();
        foreach (var sender in senders)
        {
            if (!byChannel.TryAdd(sender.Channel, sender))
            {
                throw new InvalidOperationException($"More than one reply sender is registered for the {sender.Channel} channel.");
            }
        }

        _senders = byChannel;
        _logger = logger;
    }

    public Task SendAsync(ChatAddress chat, string text, CancellationToken cancellationToken = default)
    {
        if (_senders.TryGetValue(chat.Channel, out var sender))
        {
            return sender.SendAsync(chat.ChatId, text, cancellationToken);
        }

        // The channel is switched off (or not built yet). The note is already durable, so this
        // costs only the confirmation. Logged without the text: a reply can quote vault content.
        _logger.LogWarning("No reply sender is registered for the {Channel} channel; dropping a reply to {Chat}.", chat.Channel, chat);
        return Task.CompletedTask;
    }
}
