namespace MindBot.Core.Durability;

/// <summary>
/// One inbound message's worth of state changes, applied atomically.
/// <para>
/// An inbound message is identified by its <see cref="Channel"/> plus an id that channel assigns
/// (Telegram's update_id, or the web channel's own message id). Ids are only unique within a
/// channel, so every dedupe check and record takes both.
/// </para>
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
    Task<bool> IsUpdateProcessedAsync(Channel channel, long updateId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the chat's live conversation state, or <see cref="ConversationState.None"/> when
    /// there is none or the stored one has passed its expiry.
    /// </summary>
    Task<ConversationState> GetConversationAsync(ChatAddress chat, CancellationToken cancellationToken = default);

    Task SetConversationAsync(ChatAddress chat, ConversationState state, CancellationToken cancellationToken = default);

    Task ClearConversationAsync(ChatAddress chat, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves <paramref name="baseFilename"/> to a name that collides with neither an existing
    /// note nor a filename already reserved by a pending job, appending -2, -3, ... as needed.
    /// </summary>
    Task<string> ReserveFilenameAsync(string baseFilename, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the most up-to-date content for a note addressed by <paramref name="relativeFolder"/>
    /// and <paramref name="filename"/>: the content of the latest pending write job targeting that
    /// path if one exists (it has not reached disk yet, so it is the authoritative state), else
    /// the file's content on disk, else null if the note does not exist yet.
    /// </summary>
    Task<string?> GetLatestNoteContentAsync(string relativeFolder, string filename, CancellationToken cancellationToken = default);

    Task EnqueueWriteJobAsync(
        long updateId,
        string relativeFolder,
        string filename,
        string content,
        ChatAddress chat,
        long senderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records work that cannot finish inside this transaction — see <see cref="BackgroundJob"/>.
    /// Enqueuing here rather than from the worker is what extends the duplicate guard to deferred
    /// work: the job and the processed-update row become durable together or not at all.
    /// </summary>
    Task EnqueueBackgroundJobAsync(
        long updateId,
        string kind,
        string payload,
        ChatAddress chat,
        long senderId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a background job done. Called by the worker in the same transaction that enqueues the
    /// job's resulting write job, so a crash between the two cannot replay a completed pipeline and
    /// file its note a second time.
    /// </summary>
    Task CompleteBackgroundJobAsync(long jobId, CancellationToken cancellationToken = default);

    Task MarkUpdateProcessedAsync(Channel channel, long updateId, CancellationToken cancellationToken = default);

    Task SetTelegramOffsetAsync(int offset, CancellationToken cancellationToken = default);

    /// <summary>Commits the transaction. Nothing in this unit of work is durable until it returns.</summary>
    Task CommitAsync(CancellationToken cancellationToken = default);
}

public interface IIngestUnitOfWorkFactory
{
    Task<IIngestUnitOfWork> BeginAsync(CancellationToken cancellationToken = default);
}
