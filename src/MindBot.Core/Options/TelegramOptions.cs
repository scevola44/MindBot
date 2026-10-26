namespace MindBot.Core.Options;

/// <summary>
/// Bound from the TELEGRAM__ environment variable prefix. The channel is optional: leaving
/// <see cref="BotToken"/> unset switches Telegram off entirely (no client, no poller).
/// </summary>
public sealed class TelegramOptions
{
    public const string SectionName = "TELEGRAM";

    public string BotToken { get; set; } = string.Empty;

    public bool IsEnabled => !string.IsNullOrWhiteSpace(BotToken);

    /// <summary>Comma-separated list of Telegram numeric user IDs allowed to use the bot.</summary>
    public string AllowedUserIds { get; set; } = string.Empty;

    /// <summary>
    /// Chat that receives operational alerts (branch rewritten, push failing). Optional: when
    /// unset the alerts are logged instead of sent, so the bot still runs without it.
    /// </summary>
    public long? OperatorChatId { get; set; }

    public IReadOnlySet<long> ParseAllowedUserIds()
    {
        var ids = new HashSet<long>();
        foreach (var part in AllowedUserIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (long.TryParse(part, out var id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }
}
