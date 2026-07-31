namespace MindBot.Core.Options;

/// <summary>Bound from the STATE__ environment variable prefix. Configures the SQLite
/// durability store that survives restarts and crashes.</summary>
public sealed class StateOptions
{
    public const string SectionName = "STATE";

    /// <summary>
    /// Absolute path to the SQLite database file. Must live outside <see cref="VaultOptions.Root"/>
    /// so the bot's own state is never committed into the vault repository.
    /// </summary>
    public string DatabasePath { get; set; } = "/data/mindbot.db";

    /// <summary>
    /// How long a half-finished /new conversation survives before it is discarded. Prevents a
    /// forgotten conversation from swallowing an unrelated message sent hours later.
    /// </summary>
    public int ConversationExpiryMinutes { get; set; } = 60;

    /// <summary>
    /// How long processed update IDs are retained. Telegram itself only retains undelivered
    /// updates for about 24 hours, so anything beyond a few days is dead weight.
    /// </summary>
    public int ProcessedUpdateRetentionDays { get; set; } = 7;
}
