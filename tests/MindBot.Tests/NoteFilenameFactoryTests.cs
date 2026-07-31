using MindBot.Core.Notes;

namespace MindBot.Tests;

public class NoteFilenameFactoryTests
{
    [Fact]
    public void CreateFromTimestamp_BuildsMinutePrecisionFilename()
    {
        var created = new DateTimeOffset(2026, 7, 30, 9, 5, 30, TimeSpan.Zero);

        var filename = NoteFilenameFactory.CreateFromTimestamp(created);

        Assert.Equal("202607300905.md", filename);
    }

    [Fact]
    public void CreateFromTimestamp_EndsWithMarkdownExtension()
    {
        var filename = NoteFilenameFactory.CreateFromTimestamp(DateTimeOffset.Now);

        Assert.EndsWith(".md", filename);
    }

    [Fact]
    public void CreateFromName_SanitizesAndAppendsExtension()
    {
        var filename = NoteFilenameFactory.CreateFromName("My Great Note!");

        Assert.Equal("my-great-note.md", filename);
    }

    [Fact]
    public void CreateFromName_PathTraversalName_ProducesSafeFilename()
    {
        var filename = NoteFilenameFactory.CreateFromName("../../etc/passwd");

        Assert.DoesNotContain("..", filename);
        Assert.DoesNotContain("/", filename);
        Assert.EndsWith(".md", filename);
    }
}
