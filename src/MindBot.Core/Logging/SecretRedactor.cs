using System.Text.RegularExpressions;

namespace MindBot.Core.Logging;

/// <summary>
/// Strips the Telegram bot token out of anything about to be logged.
/// <para>
/// The token is not only a configuration value: Telegram embeds it in every file-download URL
/// (<c>https://api.telegram.org/file/bot&lt;token&gt;/...</c>), so an exception message or a
/// logged URL can leak it even though nothing ever logs the setting itself. Both shapes are
/// redacted here, and the URL pattern is matched independently of the configured token so a
/// token from any source is caught.
/// </para>
/// </summary>
public sealed partial class SecretRedactor
{
    private const string Placeholder = "***";

    private readonly string? _botToken;

    public SecretRedactor(string? botToken)
    {
        // A blank or trivially short token would turn every log line into asterisks.
        _botToken = string.IsNullOrWhiteSpace(botToken) || botToken.Length < 8 ? null : botToken;
    }

    [GeneratedRegex(@"(?<prefix>https?://[^\s/]*telegram[^\s/]*/(?:file/)?bot)[^\s/]+", RegexOptions.IgnoreCase)]
    private static partial Regex TelegramUrlTokenRegex();

    public string? Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var redacted = TelegramUrlTokenRegex().Replace(value, $"${{prefix}}{Placeholder}");

        if (_botToken is not null)
        {
            redacted = redacted.Replace(_botToken, Placeholder, StringComparison.Ordinal);
        }

        return redacted;
    }
}
