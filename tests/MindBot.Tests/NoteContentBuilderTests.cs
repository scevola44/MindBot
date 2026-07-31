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
        Assert.Contains("date: 2026-07-30T08:15:00-04:00", content);
        Assert.Contains("- WIP", content);
        Assert.Contains("- MindBot", content);
        Assert.DoesNotContain("source:", content);
        Assert.DoesNotContain("fleeting", content);
        Assert.EndsWith("Remember to call the dentist.\n", content);
    }

    [Fact]
    public void Build_TransformsDollarShorthandIntoWikilinks()
    {
        var created = new DateTimeOffset(2026, 7, 30, 8, 15, 0, TimeSpan.FromHours(-4));

        var content = NoteContentBuilder.Build("Call $Alice about $(the trip)", created);

        Assert.EndsWith("Call [[Alice]] about [[the trip]]\n", content);
    }
}
