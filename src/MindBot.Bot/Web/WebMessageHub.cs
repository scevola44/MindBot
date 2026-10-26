using System.Collections.Concurrent;
using System.Threading.Channels;

namespace MindBot.Bot.Web;

/// <summary>
/// Tells connected WebSocket sessions that the transcript has grown. A doorbell, not a pipe: it
/// carries no messages.
/// <para>
/// Sessions always read what to send from the transcript in SQLite, after the last id they sent.
/// That matters because lines are announced after their transaction commits, and two concurrent
/// requests can announce out of order (7 before 5). Delivering the announced message itself would
/// then let a session skip 5, or a client resume after 7 and never see 5. Reading from the store
/// instead always yields a gap-free, ordered run: SQLite has one writer, so ids commit in order.
/// </para>
/// <para>
/// Each subscription holds at most one pending ring, so a burst of messages coalesces into a
/// single read and a slow session can never make the hub buffer without bound.
/// </para>
/// </summary>
public sealed class WebMessageHub
{
    private readonly ConcurrentDictionary<Subscription, byte> _subscriptions = new();

    public Subscription Subscribe()
    {
        var subscription = new Subscription(this);
        _subscriptions.TryAdd(subscription, 0);
        return subscription;
    }

    /// <summary>Call after a transcript line has been committed.</summary>
    public void NotifyNewMessages()
    {
        foreach (var subscription in _subscriptions.Keys)
        {
            // A full channel already has a ring pending, which will pick this line up too.
            subscription.Writer.TryWrite(true);
        }
    }

    public int SubscriberCount => _subscriptions.Count;

    private void Remove(Subscription subscription) => _subscriptions.TryRemove(subscription, out _);

    public sealed class Subscription : IDisposable
    {
        private readonly WebMessageHub _hub;
        private readonly Channel<bool> _rings = Channel.CreateBounded<bool>(
            new BoundedChannelOptions(1) { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite });

        internal Subscription(WebMessageHub hub) => _hub = hub;

        public ChannelReader<bool> Rings => _rings.Reader;

        internal ChannelWriter<bool> Writer => _rings.Writer;

        public void Dispose()
        {
            _hub.Remove(this);
            _rings.Writer.TryComplete();
        }
    }
}
