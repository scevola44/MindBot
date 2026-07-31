using System.Text.Json;
using MindBot.Core.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace MindBot.Bot.Logging;

/// <summary>
/// Emits one JSON object per log entry, with the bot token stripped from every field.
/// <para>
/// Redaction lives in the formatter rather than at individual call sites because the requirement
/// is that the token never appears in a log line <em>anywhere</em>. Call-site redaction only
/// covers the sites someone remembered; a formatter is the single boundary every message must
/// pass through, including exception text thrown from inside Telegram.Bot or HttpClient, which is
/// where a file-download URL with an embedded token would otherwise surface.
/// </para>
/// </summary>
public sealed class RedactingConsoleFormatter(SecretRedactor redactor)
    : ConsoleFormatter(FormatterName)
{
    public const string FormatterName = "mindbot-redacting";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        var formatter = logEntry.Formatter;
        var message = formatter is null ? null : formatter(logEntry.State, logEntry.Exception);
        if (string.IsNullOrEmpty(message) && logEntry.Exception is null)
        {
            return;
        }

        var payload = new Dictionary<string, object?>
        {
            ["timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
            ["level"] = logEntry.LogLevel.ToString(),
            ["category"] = logEntry.Category,
            ["message"] = redactor.Redact(message),
        };

        if (logEntry.EventId.Id != 0)
        {
            payload["eventId"] = logEntry.EventId.Id;
        }

        if (logEntry.Exception is not null)
        {
            payload["exception"] = redactor.Redact(logEntry.Exception.ToString());
        }

        AppendScopes(scopeProvider, payload);

        textWriter.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private void AppendScopes(IExternalScopeProvider? scopeProvider, Dictionary<string, object?> payload)
    {
        scopeProvider?.ForEachScope(
            (scope, state) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
                {
                    foreach (var pair in pairs)
                    {
                        // The structured-logging template itself is noise in the output.
                        if (pair.Key == "{OriginalFormat}")
                        {
                            continue;
                        }

                        state[pair.Key] = redactor.Redact(pair.Value?.ToString());
                    }
                }
                else if (scope is not null)
                {
                    state["scope"] = redactor.Redact(scope.ToString());
                }
            },
            payload);
    }
}
