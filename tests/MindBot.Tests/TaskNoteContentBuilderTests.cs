using MindBot.Core.Notes;

namespace MindBot.Tests;

public class TaskNoteContentBuilderTests
{
    [Fact]
    public void Append_NoExistingNote_CreatesFrontmatterAndSingleItem()
    {
        var now = new DateTimeOffset(2026, 7, 31, 10, 0, 0, TimeSpan.Zero);

        var content = TaskNoteContentBuilder.Append(null, ["Buy groceries"], now);

        Assert.StartsWith("---\n", content);
        Assert.Contains("date: 2026-07-31T10:00", content);
        Assert.Contains("last-modified: 2026-07-31T10:00", content);
        Assert.Contains("- ToDo", content);
        Assert.EndsWith("---\n- [ ] Buy groceries\n", content);
    }

    [Fact]
    public void Append_ExistingNote_PreservesDate_UpdatesLastModified_AppendsItem()
    {
        var existing =
            "---\n" +
            "date: 2026-07-30T11:24\n" +
            "tags:\n" +
            "  - ToDo\n" +
            "---\n" +
            "- [ ] Send mail\n";
        var now = new DateTimeOffset(2026, 7, 31, 9, 0, 0, TimeSpan.Zero);

        var content = TaskNoteContentBuilder.Append(existing, ["Buy groceries"], now);

        Assert.Contains("date: 2026-07-30T11:24", content);
        Assert.Contains("last-modified: 2026-07-31T09:00", content);
        Assert.Contains("- [ ] Send mail", content);
        Assert.Contains("- [ ] Buy groceries", content);
        Assert.True(content.IndexOf("Send mail", StringComparison.Ordinal) < content.IndexOf("Buy groceries", StringComparison.Ordinal));
    }

    [Fact]
    public void Append_MultipleItems_AddsOneChecklistLineEach()
    {
        var now = new DateTimeOffset(2026, 7, 31, 10, 0, 0, TimeSpan.Zero);

        var content = TaskNoteContentBuilder.Append(null, ["Task number 1", "Task number 2", "Task number 3"], now);

        Assert.Contains("- [ ] Task number 1", content);
        Assert.Contains("- [ ] Task number 2", content);
        Assert.Contains("- [ ] Task number 3", content);
    }

    [Fact]
    public void Append_ExistingContentWithoutFrontmatter_KeepsContentAsBody()
    {
        var existing = "just a plain line, no frontmatter";
        var now = new DateTimeOffset(2026, 7, 31, 10, 0, 0, TimeSpan.Zero);

        var content = TaskNoteContentBuilder.Append(existing, ["Buy groceries"], now);

        Assert.Contains("just a plain line, no frontmatter", content);
        Assert.Contains("- [ ] Buy groceries", content);
        Assert.Contains("date: 2026-07-31T10:00", content);
    }

    [Fact]
    public void Append_TransformsDollarShorthandInNewItemsOnly()
    {
        var existing =
            "---\n" +
            "date: 2026-07-30T11:24\n" +
            "tags:\n" +
            "  - ToDo\n" +
            "---\n" +
            "- [ ] Call $Alice\n";
        var now = new DateTimeOffset(2026, 7, 31, 10, 0, 0, TimeSpan.Zero);

        var content = TaskNoteContentBuilder.Append(existing, ["Call $Bob"], now);

        Assert.Contains("- [ ] Call $Alice", content);
        Assert.Contains("- [ ] Call [[Bob]]", content);
    }
}
