using System.Text.RegularExpressions;

namespace MindBot.Core.Notes;

/// <summary>
/// Extracts <c>#tag</c> tokens from a message body. A tag must start with a letter (so a bare
/// <c>#</c> or a numeric anchor like <c>#1</c> is left as plain text) and may contain internal
/// letters, digits, underscores or hyphens. Returns distinct tag names, in first-seen order, with
/// the leading <c>#</c> stripped.
/// </summary>
public static partial class HashtagExtractor
{
    public static IReadOnlyList<string> Extract(string text) =>
        HashtagToken().Matches(text)
            .Select(static m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    [GeneratedRegex(@"#([A-Za-z][\w-]*)")]
    private static partial Regex HashtagToken();
}
