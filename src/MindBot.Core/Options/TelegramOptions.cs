namespace MindBot.Core.Options;

/// <summary>Bound from the TELEGRAM__ environment variable prefix.</summary>
public sealed class TelegramOptions
{
    public const string SectionName = "TELEGRAM";

    public string BotToken { get; set; } = string.Empty;

    /// <summary>Comma-separated list of Telegram numeric user IDs allowed to use the bot.</summary>
    public string AllowedUserIds { get; set; } = string.Empty;

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
