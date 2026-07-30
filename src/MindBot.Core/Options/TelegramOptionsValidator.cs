using Microsoft.Extensions.Options;

namespace MindBot.Core.Options;

public sealed class TelegramOptionsValidator : IValidateOptions<TelegramOptions>
{
    public ValidateOptionsResult Validate(string? name, TelegramOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.BotToken))
        {
            errors.Add("TELEGRAM__BOTTOKEN is required but was not set.");
        }

        if (string.IsNullOrWhiteSpace(options.AllowedUserIds))
        {
            errors.Add("TELEGRAM__ALLOWEDUSERIDS is required but was not set.");
        }
        else
        {
            var parts = options.AllowedUserIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0 || parts.Any(p => !long.TryParse(p, out _)))
            {
                errors.Add("TELEGRAM__ALLOWEDUSERIDS must be a comma-separated list of numeric Telegram user IDs.");
            }
        }

        return errors.Count > 0 ? ValidateOptionsResult.Fail(errors) : ValidateOptionsResult.Success;
    }
}
