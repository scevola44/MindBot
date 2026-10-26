using MindBot.Core.Durability;

namespace MindBot.Core.Web;

/// <summary>
/// The single web conversation. MindBot has one user, so every authorised device reads and
/// writes the same chat: history, /new state and deferred replies follow the user across
/// devices instead of being split per device.
/// </summary>
public static class WebChat
{
    public const long ChatId = 1;

    public static ChatAddress Address { get; } = new(Channel.Web, ChatId);
}

public enum WebMessageDirection
{
    /// <summary>Sent by the user from a device.</summary>
    In = 0,

    /// <summary>A reply from the bot, immediate or deferred.</summary>
    Out = 1,

    /// <summary>An operator alert (push failing, branch rewritten) posted into the chat.</summary>
    System = 2,
}

/// <summary>
/// One line of the web transcript. <see cref="Id"/> is assigned by the store, only ever grows,
/// and is the cursor clients sync from.
/// </summary>
public sealed record WebMessage(
    long Id,
    string? ClientMessageId,
    WebMessageDirection Direction,
    string Text,
    DateTimeOffset CreatedAt,
    long? InReplyToId);
