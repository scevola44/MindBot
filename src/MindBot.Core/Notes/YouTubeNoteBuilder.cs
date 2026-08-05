using MindBot.Core.YouTube;

namespace MindBot.Core.Notes;

/// <summary>The frontmatter object and body text of a YouTube summary note, before YAML serialization.</summary>
public sealed record YouTubeNote(string BaseFilename, YouTubeNoteFrontmatter Frontmatter, string Body);

/// <summary>
/// Turns a completed <see cref="YouTubeSummaryResult"/> into the note MindBot files under
/// <see cref="VaultLayout.FleetingFolder"/>.
/// <para>
/// Builds the frontmatter as an object and hands it to CreateNoteHandler to serialize — never as
/// YAML text. That matters most for the keywords: an unquoted "[[Foo]]" would parse as a nested
/// flow sequence, and only the emitter knows to quote it.
/// </para>
/// </summary>
public static class YouTubeNoteBuilder
{
    /// <summary>Minute precision and no UTC offset, matching the daily task note's "date" field.</summary>
    private const string DateFormat = "yyyy-MM-ddTHH:mm";

    /// <summary>
    /// Verbatim configuration block for Obsidian's Table of Contents plugin. Reproduced exactly,
    /// comments included: the plugin parses this as YAML and the comments are what document the
    /// options to a reader editing the note by hand.
    /// </summary>
    private const string TableOfContentsBlock = """
        ```table-of-contents
        title: ## Table of contents
        style: nestedList # TOC style (nestedList|nestedOrderedList|inlineFirstLevel)
        minLevel: 2 # Include headings from the specified level
        maxLevel: # Include headings up to the specified level
        includeLinks: true # Make headings clickable
        hideWhenEmpty: true # Hide TOC if no headings are found
        debugInConsole: false # Print debug info in Obsidian console
        ```
        """;

    public static YouTubeNote Build(YouTubeSummaryResult summary, DateTimeOffset created)
    {
        var frontmatter = new YouTubeNoteFrontmatter
        {
            Date = created.ToString(DateFormat),
            Keywords = ToWikilinks(summary.Keywords),
        };

        var body = string.Join(
            '\n',
            $"# {summary.Title}",
            $"*Source [YouTube]({summary.VideoUrl})*",
            string.Empty,
            TableOfContentsBlock,
            string.Empty,
            summary.Summary.TrimEnd());

        return new YouTubeNote(NoteFilenameFactory.CreateFromName(summary.Title), frontmatter, body);
    }

    /// <summary>
    /// Wraps each keyword as a wikilink, skipping blanks and case-insensitive duplicates. A keyword
    /// the model already wrapped is passed through rather than double-wrapped.
    /// </summary>
    private static List<string> ToWikilinks(IReadOnlyList<string> keywords)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var links = new List<string>(keywords.Count);

        foreach (var keyword in keywords)
        {
            var trimmed = keyword.Trim();
            if (trimmed.StartsWith("[[", StringComparison.Ordinal) && trimmed.EndsWith("]]", StringComparison.Ordinal))
            {
                trimmed = trimmed[2..^2].Trim();
            }

            if (trimmed.Length > 0 && seen.Add(trimmed))
            {
                links.Add($"[[{trimmed}]]");
            }
        }

        return links;
    }
}
