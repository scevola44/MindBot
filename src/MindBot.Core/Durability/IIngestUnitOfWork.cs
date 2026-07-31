namespace MindBot.Core.Durability;

/// <summary>
/// One Telegram update's worth of state changes, applied atomically.
/// <para>
/// This is the heart of the duplicate guard. Recording that an update was processed, mutating
/// the conversation state, reserving a filename and enqueuing the write job all land in a single
/// SQLite transaction. A crash before <see cref="CommitAsync"/> leaves no trace and Telegram
/// redelivers; a crash after it leaves the update marked processed, so the redelivery is
/// skipped. There is no window in which a note is written twice or dropped.
/// </para>
/// </summary>
public interface IIngestUnitOfWork : IAsyncDisposable
{
    /// <summary>True when this update has already been accepted, in which case it must be skipped entirely.</summary>
    Task<bool> IsUpdateProcessedAsync(long updateId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the chat's live conversation state, or <see cref="ConversationState.None"/> when
    /// there is none or the stored one has passed its expiry.
    /// </summary>
    Task<ConversationState> GetConversationAsync(long chatId, CancellationToken cancellationToken = default);

    Task SetConversationAsync(long chatId, ConversationState state, CancellationToken cancellationToken = default);

    Task ClearConversationAsync(long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves <paramref name="baseFilename"/> to a name that collides with neither an existing
    /// note nor a filename already reserved by a pending job, appending -2, -3, ... as needed.
    /// </summary>
    Task<string> ReserveFilenameAsync(string baseFilename, CancellationToken cancellationToken = default);

    Task EnqueueWriteJobAsync(
        long updateId,
        string filename,
        string content,
        long chatId,
        long senderId,
        CancellationToken cancellationToken = default);

    Task MarkUpdateProcessedAsync(long updateId, CancellationToken cancellationToken = default);

    Task SetTelegramOffsetAsync(int offset, CancellationToken cancellationToken = default);

    /// <summary>Commits the transaction. Nothing in this unit of work is durable until it returns.</summary>
    Task CommitAsync(CancellationToken cancellationToken = default);
}

public interface IIngestUnitOfWorkFactory
{
    Task<IIngestUnitOfWork> BeginAsync(CancellationToken cancellationToken = default);
}
