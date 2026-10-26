using System.Text.RegularExpressions;

namespace MindBot.Core.Logging;

/// <summary>
/// Strips secrets out of anything about to be logged: the Telegram bot token, and web device
/// tokens.
/// <para>
/// The token is not only a configuration value: Telegram embeds it in every file-download URL
/// (<c>https://api.telegram.org/file/bot&lt;token&gt;/...</c>), so an exception message or a
/// logged URL can leak it even though nothing ever logs the setting itself. Both shapes are
/// redacted here, and the URL pattern is matched independently of the configured token so a
/// token from any source is caught.
/// </para>
/// <para>
/// Web device tokens are matched by their fixed <c>mbt_</c> prefix, so a token is redacted
/// wherever it surfaces (a pasted token in a message, an exception, a malformed header) without
/// the redactor needing to know which tokens exist.
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

    // Deliberately looser than DeviceTokens.LooksValid: a truncated or mangled token is still a
    // secret worth hiding.
    [GeneratedRegex(@"mbt_[A-Za-z0-9_\-]{8,}")]
    private static partial Regex DeviceTokenRegex();

    public string? Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var redacted = TelegramUrlTokenRegex().Replace(value, $"${{prefix}}{Placeholder}");
        redacted = DeviceTokenRegex().Replace(redacted, "mbt_" + Placeholder);

        if (_botToken is not null)
        {
            redacted = redacted.Replace(_botToken, Placeholder, StringComparison.Ordinal);
        }

        return redacted;
    }
}
