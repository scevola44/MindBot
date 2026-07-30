using MindBot.Core.Notes;

namespace MindBot.Tests;

public class NoteContentBuilderTests
{
    [Fact]
    public void Build_IncludesFrontmatterAndVerbatimBody()
    {
        var created = new DateTimeOffset(2026, 7, 30, 8, 15, 0, TimeSpan.FromHours(-4));

        var content = NoteContentBuilder.Build("Remember to call the dentist.", created);

        Assert.StartsWith("---\n", content);
        Assert.Contains("source: telegram", content);
        Assert.Contains("- fleeting", content);
        Assert.Contains("created: 2026-07-30T08:15:00-04:00", content);
        Assert.EndsWith("Remember to call the dentist.\n", content);
    }
}
