namespace MindBot.Core.Notes;

/// <summary>
/// Locates a note's frontmatter block by verbatim substring so it can be carried forward
/// untouched by callers that must not lose unknown keys, key order, or comments. Never parses the
/// YAML into an object and re-emits it -- that would silently drop anything the object model
/// doesn't know about. LF-only, matching every other writer in this codebase
/// (NoteContentBuilder/TaskNoteContentBuilder both hardcode "\n").
/// </summary>
public static class NoteFrontmatterSplitter
{
    private const string Delimiter = "---\n";

    /// <summary>
    /// Splits <paramref name="content"/> into the frontmatter block (delimiters included, verbatim)
    /// and the body that follows it, or null if <paramref name="content"/> doesn't start with a
    /// "---" block terminated by another "---" line.
    /// </summary>
    public static (string FrontmatterBlockVerbatim, string Body)? Split(string content)
    {
        if (!content.StartsWith(Delimiter, StringComparison.Ordinal))
        {
            return null;
        }

        var closing = content.IndexOf("\n---\n", Delimiter.Length, StringComparison.Ordinal);
        if (closing < 0)
        {
            return null;
        }

        var frontmatterBlockVerbatim = content[..(closing + "\n---\n".Length)];
        var body = content[(closing + "\n---\n".Length)..];
        return (frontmatterBlockVerbatim, body);
    }

    /// <summary>The YAML text strictly between the two "---" delimiter lines, for validation only -- never for re-emission.</summary>
    public static string InnerYaml(string frontmatterBlockVerbatim) =>
        frontmatterBlockVerbatim[Delimiter.Length..^Delimiter.Length];
}
