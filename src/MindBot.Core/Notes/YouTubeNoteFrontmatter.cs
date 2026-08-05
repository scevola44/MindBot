namespace MindBot.Core.Notes;

/// <summary>
/// Frontmatter for an AI-summarised YouTube note.
/// <para>
/// Property declaration order is emit order in YamlDotNet, so tags/date/keywords here is what puts
/// the keys in that order in the file. Do not reorder these to match
/// <see cref="NoteFrontmatter"/>.
/// </para>
/// </summary>
public sealed class YouTubeNoteFrontmatter
{
    public List<string> Tags { get; set; } = ["WIP", "Youtube", "AISummary"];

    public string Date { get; set; } = string.Empty;

    /// <summary>Each entry is already wrapped as an Obsidian wikilink by <see cref="YouTubeNoteBuilder"/>.</summary>
    public List<string> Keywords { get; set; } = [];
}
