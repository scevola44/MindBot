using MindBot.Core.Notes;

namespace MindBot.Tests;

public class NoteFrontmatterSplitterTests
{
    [Fact]
    public void Split_NestedFrontmatterCommentAndBulletedBody_RoundTripsByteExact()
    {
        var content = string.Join('\n',
            "---",
            "date: 2026-07-30T09:00:00+00:00",
            "# a frontmatter comment",
            "tags:",
            "  - WIP",
            "  - MindBot",
            "unknownKey: value",
            "nested:",
            "  inner: 1",
            "  other: two",
            "---",
            "",
            "- item one",
            "- item two",
            "");

        var result = NoteFrontmatterSplitter.Split(content);

        Assert.NotNull(result);
        var (frontmatter, body) = result.Value;
        Assert.Equal(frontmatter + body, content);
        Assert.StartsWith("---\n", frontmatter);
        Assert.EndsWith("---\n", frontmatter);
        Assert.Contains("# a frontmatter comment", frontmatter);
        Assert.Contains("unknownKey: value", frontmatter);
        Assert.Contains("- item one", body);
    }

    [Fact]
    public void Split_NoLeadingDelimiter_ReturnsNull()
    {
        Assert.Null(NoteFrontmatterSplitter.Split("just some text\nno frontmatter here\n"));
    }

    [Fact]
    public void Split_UnterminatedFrontmatter_ReturnsNull()
    {
        Assert.Null(NoteFrontmatterSplitter.Split("---\ndate: 2026-07-30\nno closing delimiter\n"));
    }

    [Fact]
    public void InnerYaml_StripsOnlyTheDelimiters()
    {
        const string block = "---\ndate: 2026-07-30\n---\n";

        var inner = NoteFrontmatterSplitter.InnerYaml(block);

        Assert.Equal("date: 2026-07-30\n", inner);
    }
}
