using MindBot.Core.Durability;
using MindBot.Core.Web;

namespace MindBot.Infrastructure.State;

/// <summary>
/// An inbound message the bot has fully accepted. Existence of this row is the duplicate guard: it
/// is written in the same transaction as the resulting write job, so a crash can never leave one
/// without the other. Keyed on (<see cref="Channel"/>, <see cref="UpdateId"/>) because each channel
/// numbers its own messages.
/// </summary>
public sealed class ProcessedUpdateEntity
{
    public Channel Channel { get; set; }

    public long UpdateId { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }
}

public sealed class WriteJobEntity
{
    public long Id { get; set; }

    public long UpdateId { get; set; }

    public string RelativeFolder { get; set; } = string.Empty;

    public string Filename { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public Channel Channel { get; set; }

    public long ChatId { get; set; }

    public long SenderId { get; set; }

    public DateTimeOffset EnqueuedAt { get; set; }

    public WriteJobStatus Status { get; set; }

    public WriteJob ToDomain() => new(Id, UpdateId, RelativeFolder, Filename, Content, new ChatAddress(Channel, ChatId), SenderId, EnqueuedAt, Status);
}

public sealed class BackgroundJobEntity
{
    public long Id { get; set; }

    public long UpdateId { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public Channel Channel { get; set; }

    public long ChatId { get; set; }

    public long SenderId { get; set; }

    public BackgroundJobStatus Status { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset EnqueuedAt { get; set; }

    /// <summary>Earliest time the worker may claim this job. Advanced by the retry backoff after a failure.</summary>
    public DateTimeOffset NextAttemptAt { get; set; }

    public BackgroundJob ToDomain() =>
        new(Id, UpdateId, Kind, Payload, new ChatAddress(Channel, ChatId), SenderId, Status, Attempts, LastError, EnqueuedAt, NextAttemptAt);
}

public sealed class ConversationStateEntity
{
    public Channel Channel { get; set; }

    public long ChatId { get; set; }

    public ConversationStage Stage { get; set; }

    public string? PendingNoteName { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Single-row table; <see cref="Id"/> is always <see cref="SingletonId"/>.</summary>
public sealed class RepositoryStateEntity
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>
    /// The last SHA this bot pushed. The classifier compares it against origin to distinguish an
    /// operator who advanced the branch from one who rewrote it after triage.
    /// </summary>
    public string? LastPushedSha { get; set; }

    public int LastTelegramOffset { get; set; }

    public DateTimeOffset? LastSuccessfulPushAt { get; set; }
}

/// <summary>
/// One line of the web transcript. Doubles as the web channel's outbox: a deferred reply is
/// written here and waits for the client to sync, so it survives the client being offline.
/// </summary>
public sealed class WebMessageEntity
{
    public long Id { get; set; }

    /// <summary>The device-generated id of an inbound message; unique, and the web channel's dedupe key.</summary>
    public string? ClientMessageId { get; set; }

    public WebMessageDirection Direction { get; set; }

    public string Text { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>For an immediate reply, the inbound line it answers, so a retried send can return it.</summary>
    public long? InReplyToId { get; set; }

    public WebMessage ToDomain() => new(Id, ClientMessageId, Direction, Text, CreatedAt, InReplyToId);
}

/// <summary>A device allowed to use the web channel. Only the token's SHA-256 hash is stored.</summary>
public sealed class DeviceTokenEntity
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastSeenAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public DeviceInfo ToDomain() => new(Id, Name, CreatedAt, LastSeenAt, RevokedAt);
}
