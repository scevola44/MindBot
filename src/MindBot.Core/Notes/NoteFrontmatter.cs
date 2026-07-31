namespace MindBot.Core.Notes;

public sealed class NoteFrontmatter
{
    public string Date { get; set; } = string.Empty;

    public List<string> Tags { get; set; } = ["WIP", "MindBot"];
}
