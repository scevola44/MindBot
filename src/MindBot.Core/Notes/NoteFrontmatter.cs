namespace MindBot.Core.Notes;

public sealed class NoteFrontmatter
{
    public string Created { get; set; } = string.Empty;

    public string Source { get; set; } = "telegram";

    public List<string> Tags { get; set; } = ["fleeting"];
}
