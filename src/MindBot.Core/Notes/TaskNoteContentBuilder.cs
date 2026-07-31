using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MindBot.Core.Notes;

/// <summary>
/// Builds and updates the single daily task note. Serializes and parses frontmatter with
/// YamlDotNet; never build or patch YAML by string manipulation.
/// <para>
/// Uses <see cref="HyphenatedNamingConvention"/> so the multi-word "LastModified" property renders
/// as "last-modified" while single-word "date"/"tags" are unaffected — matching the note format
/// exactly, including the lack of a blank line between the closing "---" and the body.
/// </para>
/// </summary>
public static class TaskNoteContentBuilder
{
    private const string DateFormat = "yyyy-MM-ddTHH:mm";

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(HyphenatedNamingConvention.Instance)
        .WithIndentedSequences()
        .Build();

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(HyphenatedNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Appends <paramref name="items"/> as new checklist lines. When <paramref name="existingContent"/>
    /// is null the note is created fresh with "date" set to <paramref name="now"/>; otherwise the
    /// existing frontmatter is parsed, "date" is preserved, "last-modified" is set to
    /// <paramref name="now"/>, and the new lines are appended after the existing body.
    /// <para>
    /// If <paramref name="existingContent"/> is not in the shape this builder writes (no
    /// frontmatter, or unparsable YAML), nothing is discarded: the whole existing content is kept
    /// as body text under fresh frontmatter.
    /// </para>
    /// </summary>
    public static string Append(string? existingContent, IReadOnlyList<string> items, DateTimeOffset now)
    {
        var (frontmatter, bodyLines) = existingContent is null
            ? (new TaskNoteFrontmatter(), new List<string>())
            : Parse(existingContent);

        if (string.IsNullOrEmpty(frontmatter.Date))
        {
            frontmatter.Date = now.ToString(DateFormat);
        }

        frontmatter.LastModified = now.ToString(DateFormat);

        var newLines = items.Select(item => $"- [ ] {WikilinkTransformer.Transform(item)}");
        var yaml = Serializer.Serialize(frontmatter);
        return $"---\n{yaml}---\n{string.Join('\n', bodyLines.Concat(newLines))}\n";
    }

    private static (TaskNoteFrontmatter Frontmatter, List<string> BodyLines) Parse(string content)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var closingIndex = lines.Length > 1 ? Array.IndexOf(lines, "---", 1) : -1;

        if (lines.Length == 0 || lines[0] != "---" || closingIndex < 0)
        {
            return (new TaskNoteFrontmatter(), lines.Where(l => l.Length > 0).ToList());
        }

        var yaml = string.Join('\n', lines[1..closingIndex]);
        TaskNoteFrontmatter frontmatter;
        try
        {
            frontmatter = Deserializer.Deserialize<TaskNoteFrontmatter>(yaml) ?? new TaskNoteFrontmatter();
        }
        catch (YamlException)
        {
            frontmatter = new TaskNoteFrontmatter();
        }

        var bodyLines = lines[(closingIndex + 1)..].Where(l => l.Length > 0).ToList();
        return (frontmatter, bodyLines);
    }
}
