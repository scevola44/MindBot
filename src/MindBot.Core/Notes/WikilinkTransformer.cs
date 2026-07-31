using System.Text.RegularExpressions;

namespace MindBot.Core.Notes;

/// <summary>
/// Turns <c>$Word</c> and <c>$(Some phrase)</c> shorthand in a note body into Obsidian
/// <c>[[wikilinks]]</c>. Bare <c>$Word</c> must start with a letter (so dollar amounts like
/// <c>$50</c> are left untouched) and may contain internal hyphens; <c>$(...)</c> captures
/// everything up to the next <c>)</c> verbatim, for phrases with spaces or punctuation.
/// </summary>
public static partial class WikilinkTransformer
{
    public static string Transform(string text) =>
        WikilinkToken().Replace(text, static m =>
            $"[[{(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)}]]");

    [GeneratedRegex(@"\$(?:\(([^)\n]+)\)|([A-Za-z][\w-]*))")]
    private static partial Regex WikilinkToken();
}
