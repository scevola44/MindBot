namespace MindBot.Core.Durability;

public enum WriteJobStatus
{
    Pending = 0,
    Completed = 1,
}

/// <summary>
/// A note that has been accepted from Telegram and is durably queued for writing.
/// <para>
/// The filename and content are resolved at ingest and stored here, which is what makes replay
/// after a crash idempotent: re-running the job rewrites the same path with byte-identical
/// content instead of allocating a second note.
/// </para>
/// </summary>
public sealed record WriteJob(
    long Id,
    long UpdateId,
    string Filename,
    string Content,
    long ChatId,
    long SenderId,
    DateTimeOffset EnqueuedAt,
    WriteJobStatus Status);
