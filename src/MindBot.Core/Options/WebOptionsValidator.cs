using Microsoft.Extensions.Options;

namespace MindBot.Core.Options;

/// <summary>
/// Validates the web channel, and owns the cross-channel rule: with Telegram optional, a
/// configuration that enables neither channel would start a bot nobody can reach.
/// </summary>
public sealed class WebOptionsValidator(IOptions<TelegramOptions> telegramOptions) : IValidateOptions<WebOptions>
{
    public ValidateOptionsResult Validate(string? name, WebOptions options)
    {
        var errors = new List<string>();

        if (!options.Enabled && !telegramOptions.Value.IsEnabled)
        {
            errors.Add("No chat channel is enabled. Set TELEGRAM__BOTTOKEN (and TELEGRAM__ALLOWEDUSERIDS), or set WEB__ENABLED=true, or both.");
        }

        if (options.Enabled)
        {
            if (options.Port is < 1 or > 65535)
            {
                errors.Add($"WEB__PORT must be between 1 and 65535, got {options.Port}.");
            }
            else if (options.Port == WebOptions.HealthPort)
            {
                errors.Add($"WEB__PORT must not be {WebOptions.HealthPort}: that port serves /health and is kept in-container.");
            }

            if (!string.IsNullOrWhiteSpace(options.StaticRoot) && !Path.IsPathRooted(options.StaticRoot))
            {
                errors.Add($"WEB__STATICROOT must be an absolute path, got '{options.StaticRoot}'.");
            }
        }

        if (options.HistoryRetentionDays <= 0)
        {
            errors.Add($"WEB__HISTORYRETENTIONDAYS must be greater than zero, got {options.HistoryRetentionDays}.");
        }

        if (options.MaxMessageLength <= 0)
        {
            errors.Add($"WEB__MAXMESSAGELENGTH must be greater than zero, got {options.MaxMessageLength}.");
        }

        return errors.Count > 0 ? ValidateOptionsResult.Fail(errors) : ValidateOptionsResult.Success;
    }
}
