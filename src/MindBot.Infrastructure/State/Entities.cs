using MindBot.Core.Durability;

namespace MindBot.Infrastructure.State;

/// <summary>
/// An update Telegram has delivered and the bot has fully accepted. Existence of this row is the
/// duplicate guard: it is written in the same transaction as the resulting write job, so a crash
/// can never leave one without the other.
/// </summary>
public sealed class ProcessedUpdateEntity
{
    public long UpdateId { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }
}

public sealed class WriteJobEntity
{
    public long Id { get; set; }

    public long UpdateId { get; set; }

    public string Filename { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public long ChatId { get; set; }

    public long SenderId { get; set; }

    public DateTimeOffset EnqueuedAt { get; set; }

    public WriteJobStatus Status { get; set; }

    public WriteJob ToDomain() => new(Id, UpdateId, Filename, Content, ChatId, SenderId, EnqueuedAt, Status);
}

public sealed class ConversationStateEntity
{
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
