using MindBot.Core.Notes;

namespace MindBot.Tests;

public class NoteFilenameFactoryTests
{
    [Fact]
    public void Create_BuildsTimestampSlugFilename()
    {
        var created = new DateTimeOffset(2026, 7, 30, 12, 34, 56, TimeSpan.FromHours(2));

        var filename = NoteFilenameFactory.Create(created, "Buy milk and eggs tomorrow morning please");

        Assert.Equal("2026-07-30T123456-buy-milk-and-eggs-tomorrow-morning.md", filename);
    }

    [Fact]
    public void Create_EndsWithMarkdownExtension()
    {
        var created = DateTimeOffset.Now;
        var filename = NoteFilenameFactory.Create(created, "hello world");

        Assert.EndsWith(".md", filename);
    }
}
