using MindBot.Core.Notes;
using MindBot.Core.Operations;
using MindBot.Tests.Fakes;

namespace MindBot.Tests;

public class CreateNoteHandlerTests
{
    [Fact]
    public async Task ResolveAsync_MatchesNoteContentBuilderOutput_ForEquivalentInputs()
    {
        using var vaultRoot = new TempVaultRoot();
        var created = new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.Zero);
        var frontmatter = new NoteFrontmatter { Date = created.ToString("yyyy-MM-ddTHH:mm:sszzz") };
        const string body = "Just a quick thought";

        var context = new UnitOfWorkVaultOperationContext(new InMemoryIngestUnitOfWork(vaultRoot.Path), vaultRoot.Path);
        var handler = new CreateNoteHandler();

        var resolved = await handler.ResolveAsync(new CreateNote("05 - Fleeting/202607300900.md", frontmatter, body), context);

        var expected = NoteContentBuilder.Build(body, created);
        Assert.Equal(expected, resolved.Content);
        Assert.Equal("05 - Fleeting", resolved.RelativeFolder);
        Assert.Equal("202607300900.md", resolved.Filename);
    }

    [Fact]
    public async Task ResolveAsync_DoesNotReapplyWikilinkTransform()
    {
        using var vaultRoot = new TempVaultRoot();
        var frontmatter = new NoteFrontmatter { Date = "2026-07-30T09:00:00+00:00" };
        const string alreadyTransformedBody = "See [[Alice]] about it.";

        var context = new UnitOfWorkVaultOperationContext(new InMemoryIngestUnitOfWork(vaultRoot.Path), vaultRoot.Path);
        var handler = new CreateNoteHandler();

        var resolved = await handler.ResolveAsync(new CreateNote("05 - Fleeting/note.md", frontmatter, alreadyTransformedBody), context);

        Assert.Contains("See [[Alice]] about it.", resolved.Content);
    }

    [Fact]
    public async Task ResolveAsync_PathEscapingVaultRoot_Throws()
    {
        using var vaultRoot = new TempVaultRoot();
        var frontmatter = new NoteFrontmatter { Date = "2026-07-30T09:00:00+00:00" };
        var context = new UnitOfWorkVaultOperationContext(new InMemoryIngestUnitOfWork(vaultRoot.Path), vaultRoot.Path);
        var handler = new CreateNoteHandler();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.ResolveAsync(new CreateNote("../../etc/passwd", frontmatter, "body"), context));
    }
}
