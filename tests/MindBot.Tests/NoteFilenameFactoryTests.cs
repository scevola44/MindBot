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

    [Fact]
    public void CreateCandidate_FirstAttempt_IsTheUnadornedBaseName()
    {
        // The common case is a single message in its minute; it must not gain a suffix.
        Assert.Equal("202607300905.md", NoteFilenameFactory.CreateCandidate("202607300905.md", 1));
    }

    [Theory]
    [InlineData(2, "202607300905-2.md")]
    [InlineData(3, "202607300905-3.md")]
    [InlineData(10, "202607300905-10.md")]
    public void CreateCandidate_LaterAttempts_AppendTheAttemptNumber(int attempt, string expected)
    {
        Assert.Equal(expected, NoteFilenameFactory.CreateCandidate("202607300905.md", attempt));
    }

    [Fact]
    public void CreateCandidate_NamedNote_SuffixesBeforeTheExtension()
    {
        Assert.Equal("groceries-2.md", NoteFilenameFactory.CreateCandidate("groceries.md", 2));
    }

    [Fact]
    public void CreateCandidate_AttemptBelowOne_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NoteFilenameFactory.CreateCandidate("a.md", 0));
    }
}
