using MindBot.Core.Durability;

namespace MindBot.Core.Web;

/// <summary>
/// The ingest transaction, plus the web transcript writes that must commit with it.
/// <para>
/// Accepting a web message is the same single transaction as accepting a Telegram update, with
/// the client's message id standing in for update_id: look the id up, record the inbound line
/// (whose store-assigned id becomes the Web-channel update id), route, record the reply, mark
/// processed, commit. A retry of the same client message id therefore finds the stored reply and
/// queues nothing.
/// </para>
/// </summary>
public interface IWebIngestUnitOfWork : IIngestUnitOfWork
{
    Task<WebMessage?> FindInboundAsync(string clientMessageId, CancellationToken cancellationToken = default);

    Task<WebMessage?> FindReplyAsync(long inboundId, CancellationToken cancellationToken = default);

    Task<WebMessage> AppendAsync(
        WebMessageDirection direction,
        string text,
        string? clientMessageId = null,
        long? inReplyToId = null,
        CancellationToken cancellationToken = default);
}

public interface IWebIngestUnitOfWorkFactory
{
    Task<IWebIngestUnitOfWork> BeginWebAsync(CancellationToken cancellationToken = default);
}

/// <summary>Transcript access outside an ingest transaction: deferred replies, alerts and history.</summary>
public interface IWebTranscript
{
    /// <summary>Appends one line in its own short transaction and returns it with its id.</summary>
    Task<WebMessage> AppendAsync(WebMessageDirection direction, string text, CancellationToken cancellationToken = default);

    /// <summary>Lines with an id greater than <paramref name="afterId"/>, oldest first.</summary>
    Task<IReadOnlyList<WebMessage>> GetAfterAsync(long afterId, int limit, CancellationToken cancellationToken = default);
}
