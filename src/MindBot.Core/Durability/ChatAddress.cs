namespace MindBot.Core.Durability;

/// <summary>
/// The transport a message arrived on, and therefore the one its replies must go back through.
/// <para>
/// Stored as an INTEGER. The values are persisted, so never renumber them: every row written
/// before channels existed reads back as <see cref="Telegram"/> (0).
/// </para>
/// </summary>
public enum Channel
{
    Telegram = 0,
    Web = 1,
}

/// <summary>
/// Identifies one conversation. A chat id is only unique within its channel, so it is never
/// passed around without the channel next to it. That rule is what keeps a Telegram chat and a
/// web chat that happen to share an id from reading each other's /new state or replies.
/// </summary>
public readonly record struct ChatAddress(Channel Channel, long ChatId)
{
    public static ChatAddress Telegram(long chatId) => new(Channel.Telegram, chatId);

    public override string ToString() => $"{Channel}:{ChatId}";
}
