using Microsoft.Extensions.Options;

namespace MindBot.Core.Options;

/// <summary>
/// Validates N8N__ settings. An unset <see cref="N8nOptions.BaseUrl"/> is valid — it means "no n8n
/// in this deployment", not a misconfiguration — but a value that is set must be usable, because a
/// malformed URL would otherwise only surface minutes into a summary job.
/// </summary>
public sealed class N8nOptionsValidator : IValidateOptions<N8nOptions>
{
    public ValidateOptionsResult Validate(string? name, N8nOptions options)
    {
        var errors = new List<string>();

        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri))
            {
                errors.Add($"N8N__BASEURL must be an absolute URL, got '{options.BaseUrl}'.");
            }
            else if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                errors.Add($"N8N__BASEURL must use http or https, got '{uri.Scheme}'.");
            }
        }

        if (options.TimeoutSeconds <= 0)
        {
            errors.Add($"N8N__TIMEOUTSECONDS must be greater than zero, got {options.TimeoutSeconds}.");
        }

        if (options.MaxAttempts <= 0)
        {
            errors.Add($"N8N__MAXATTEMPTS must be greater than zero, got {options.MaxAttempts}.");
        }

        if (options.RetryBaseSeconds <= 0)
        {
            errors.Add($"N8N__RETRYBASESECONDS must be greater than zero, got {options.RetryBaseSeconds}.");
        }

        return errors.Count > 0 ? ValidateOptionsResult.Fail(errors) : ValidateOptionsResult.Success;
    }
}
