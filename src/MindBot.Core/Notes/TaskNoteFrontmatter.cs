namespace MindBot.Core.Notes;

public sealed class TaskNoteFrontmatter
{
    public string Date { get; set; } = string.Empty;

    public string LastModified { get; set; } = string.Empty;

    public List<string> Tags { get; set; } = ["ToDo"];
}
