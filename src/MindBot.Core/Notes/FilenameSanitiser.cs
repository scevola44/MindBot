using System.Text;
using System.Text.RegularExpressions;

namespace MindBot.Core.Notes;

/// <summary>
/// Turns untrusted message text into a filesystem-safe slug. A message body is untrusted
/// input: this strips path separators, leading dots, control characters and reserved
/// device names, and caps the result's length.
/// </summary>
public static partial class FilenameSanitiser
{
    private const int DefaultMaxLength = 40;
    private const int WordCount = 6;

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Builds a safe slug from the first ~6 words of the given text.</summary>
    public static string SlugFromText(string text, int maxLength = DefaultMaxLength)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Take(WordCount);
        var joined = string.Join('-', words);
        return Sanitise(joined, maxLength);
    }

    /// <summary>Strips anything unsafe for a filename component, leaving only [a-z0-9-].</summary>
    public static string Sanitise(string input, int maxLength = DefaultMaxLength)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "note";
        }

        var lowered = input.ToLowerInvariant();
        var sb = new StringBuilder(lowered.Length);

        foreach (var ch in lowered)
        {
            if (char.IsControl(ch) || ch is '/' or '\\')
            {
                continue;
            }

            sb.Append(char.IsLetterOrDigit(ch) || ch == '-' ? ch : '-');
        }

        var collapsed = MultipleHyphens().Replace(sb.ToString(), "-");
        var trimmed = collapsed.Trim('-', '.');

        if (trimmed.Length > maxLength)
        {
            trimmed = trimmed[..maxLength].Trim('-');
        }

        if (string.IsNullOrEmpty(trimmed))
        {
            trimmed = "note";
        }

        if (ReservedDeviceNames.Contains(trimmed))
        {
            trimmed = $"{trimmed}-note";
        }

        return trimmed;
    }

    [GeneratedRegex("-{2,}")]
    private static partial Regex MultipleHyphens();
}
