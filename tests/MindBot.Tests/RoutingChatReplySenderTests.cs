using MindBot.Core.Durability;
using MindBot.Core.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace MindBot.Tests;

public sealed class RoutingChatReplySenderTests
{
    private sealed class RecordingSender(Channel channel) : IChannelReplySender
    {
        public Channel Channel => channel;

        public List<(long ChatId, string Text)> Sent { get; } = [];

        public Task SendAsync(long chatId, string text, CancellationToken cancellationToken = default)
        {
            Sent.Add((chatId, text));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task AReplyGoesOnlyToItsOwnChannel()
    {
        var telegram = new RecordingSender(Channel.Telegram);
        var web = new RecordingSender(Channel.Web);
        var router = new RoutingChatReplySender([telegram, web], NullLogger<RoutingChatReplySender>.Instance);

        await router.SendAsync(new ChatAddress(Channel.Web, 42), "done");

        Assert.Equal([(42L, "done")], web.Sent);
        Assert.Empty(telegram.Sent);
    }

    [Fact]
    public async Task AReplyForAChannelWithNoSender_IsDroppedNotRedirected()
    {
        var telegram = new RecordingSender(Channel.Telegram);
        var router = new RoutingChatReplySender([telegram], NullLogger<RoutingChatReplySender>.Instance);

        await router.SendAsync(new ChatAddress(Channel.Web, 42), "vault content");

        // Falling back to another channel would leak a private reply to a transport it never used.
        Assert.Empty(telegram.Sent);
    }

    [Fact]
    public void TwoSendersForOneChannel_FailAtConstruction()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new RoutingChatReplySender(
            [new RecordingSender(Channel.Telegram), new RecordingSender(Channel.Telegram)],
            NullLogger<RoutingChatReplySender>.Instance));

        Assert.Contains("Telegram", ex.Message);
    }
}
