using MindBot.Core.Notes;

namespace MindBot.Tests;

public class VaultLayoutTests
{
    [Fact]
    public void TaskNoteFolder_BuildsYearAndPaddedMonthName()
    {
        var folder = VaultLayout.TaskNoteFolder(new DateOnly(2026, 7, 31));

        Assert.Equal(Path.Combine("06 - Daily Notes", "2026", "07 - July"), folder);
    }

    [Fact]
    public void TaskNoteFolder_PadsSingleDigitMonth()
    {
        var folder = VaultLayout.TaskNoteFolder(new DateOnly(2026, 1, 5));

        Assert.Equal(Path.Combine("06 - Daily Notes", "2026", "01 - January"), folder);
    }

    [Fact]
    public void TaskNoteFilename_UsesIsoDate()
    {
        var filename = VaultLayout.TaskNoteFilename(new DateOnly(2026, 7, 31));

        Assert.Equal("TODO - 2026-07-31.md", filename);
    }
}
