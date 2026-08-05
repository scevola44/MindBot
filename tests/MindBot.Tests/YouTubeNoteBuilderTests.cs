using MindBot.Core.Notes;
using MindBot.Core.Operations;
using MindBot.Core.YouTube;
using MindBot.Tests.Fakes;

namespace MindBot.Tests;

public sealed class YouTubeNoteBuilderTests
{
    private static readonly DateTimeOffset Created = new(2026, 8, 5, 14, 32, 17, TimeSpan.Zero);

    private static YouTubeSummaryResult SampleSummary(
        string title = "Daggerheart combat is broken",
        string summary = "## Summary\nDaggerheart combat can be swingy for new players.",
        params string[] keywords) =>
        new(
            "qIeJ7Gw9v_I",
            "https://www.youtube.com/watch?v=qIeJ7Gw9v_I",
            title,
            summary,
            keywords.Length > 0 ? keywords : ["Daggerheart", "Game Master"]);

    /// <summary>
    /// Resolves through CreateNoteHandler because that is the only thing that emits YAML — asserting
    /// on the frontmatter object alone would not catch a serializer that quotes or indents wrongly.
    /// </summary>
    private static async Task<string> ResolveAsync(YouTubeNote note, TempVaultRoot vaultRoot)
    {
        var context = new UnitOfWorkVaultOperationContext(new InMemoryIngestUnitOfWork(vaultRoot.Path), vaultRoot.Path);
        var resolved = await new CreateNoteHandler().ResolveAsync(
            new CreateNote(VaultLayout.RelativeNotePath("note.md"), note.Frontmatter, note.Body),
            context);

        return resolved.Content;
    }

    [Fact]
    public async Task ProducesTheSpecifiedNote()
    {
        using var vaultRoot = new TempVaultRoot();
        var note = YouTubeNoteBuilder.Build(SampleSummary(), Created);

        var content = await ResolveAsync(note, vaultRoot);

        Assert.Equal(
            """
            ---
            tags:
              - WIP
              - Youtube
              - AISummary
            date: 2026-08-05T14:32
            keywords:
              - '[[Daggerheart]]'
              - '[[Game Master]]'
            ---

            # Daggerheart combat is broken
            *Source [YouTube](https://www.youtube.com/watch?v=qIeJ7Gw9v_I)*

            ```table-of-contents
            title: ## Table of contents
            style: nestedList # TOC style (nestedList|nestedOrderedList|inlineFirstLevel)
            minLevel: 2 # Include headings from the specified level
            maxLevel: # Include headings up to the specified level
            includeLinks: true # Make headings clickable
            hideWhenEmpty: true # Hide TOC if no headings are found
            debugInConsole: false # Print debug info in Obsidian console
            ```

            ## Summary
            Daggerheart combat can be swingy for new players.

            """.ReplaceLineEndings("\n"),
            content);
    }

    /// <summary>
    /// The reason frontmatter is never built by string concatenation: an unquoted [[Foo]] would
    /// parse back as a nested flow sequence, not as a string.
    /// </summary>
    [Fact]
    public async Task KeywordsAreQuotedSoWikilinksSurviveAYamlRoundTrip()
    {
        using var vaultRoot = new TempVaultRoot();
        var note = YouTubeNoteBuilder.Build(SampleSummary(keywords: ["Dark Heresy"]), Created);

        var content = await ResolveAsync(note, vaultRoot);

        Assert.Contains("  - '[[Dark Heresy]]'", content);
    }

    [Fact]
    public void KeywordsAreWrappedDeduplicatedAndTrimmed()
    {
        var note = YouTubeNoteBuilder.Build(
            SampleSummary(keywords: ["  Daggerheart  ", "daggerheart", "[[Rowan Hall]]", "", "   "]),
            Created);

        Assert.Equal(["[[Daggerheart]]", "[[Rowan Hall]]"], note.Frontmatter.Keywords);
    }

    [Fact]
    public void FilenameIsSlugifiedFromTheTitle()
    {
        var note = YouTubeNoteBuilder.Build(SampleSummary(title: "Daggerheart: Combat Is BROKEN?!"), Created);

        Assert.Equal("daggerheart-combat-is-broken.md", note.BaseFilename);
    }

    /// <summary>A pathological title must not escape the fleeting folder or produce a hidden file.</summary>
    [Fact]
    public void FilenameFromAHostileTitleStaysASingleSafeComponent()
    {
        var note = YouTubeNoteBuilder.Build(SampleSummary(title: "../../etc/passwd"), Created);

        // FilenameSanitiser drops separators outright and collapses the dots, so nothing is left
        // that could be read as a path.
        Assert.Equal("etcpasswd.md", note.BaseFilename);
    }

    [Fact]
    public void ATrailingNewlineInTheSummaryDoesNotDoubleUpAtTheEndOfTheNote()
    {
        var note = YouTubeNoteBuilder.Build(SampleSummary(summary: "The summary.\n\n"), Created);

        Assert.EndsWith("The summary.", note.Body);
    }
}
