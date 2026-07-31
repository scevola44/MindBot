using MindBot.Core.Operations;
using MindBot.Tests.Fakes;

namespace MindBot.Tests;

public class AppendToNoteHandlerTests
{
    private static AppendToNoteHandler CreateHandler(DateTimeOffset? now = null) =>
        new(new FixedTimeProvider(now ?? new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.Zero)));

    [Fact]
    public async Task ResolveAsync_MissingTarget_CreatesWithMinimalFrontmatter()
    {
        using var vaultRoot = new TempVaultRoot();
        var context = new UnitOfWorkVaultOperationContext(new InMemoryIngestUnitOfWork(vaultRoot.Path), vaultRoot.Path);
        var handler = CreateHandler();

        var resolved = await handler.ResolveAsync(new AppendToNote("Scratch.md", "First entry"), context);

        Assert.Equal(string.Empty, resolved.RelativeFolder);
        Assert.Equal("Scratch.md", resolved.Filename);
        Assert.StartsWith("---\n", resolved.Content);
        Assert.Contains("date: 2026-07-30T09:00:00+00:00", resolved.Content);
        Assert.EndsWith("First entry\n", resolved.Content);
    }

    [Fact]
    public async Task ResolveAsync_ExistingTarget_PreservesUnknownKeysOrderAndComment_OnlyBodyChanges()
    {
        using var vaultRoot = new TempVaultRoot();
        var existing = string.Join('\n',
            "---",
            "zebra: 1",
            "# a comment that must survive",
            "alpha: 2",
            "nested:",
            "  inner: value",
            "---",
            "",
            "- existing bullet",
            "");
        vaultRoot.WriteFile("", "Scratch.md", existing);

        var context = new UnitOfWorkVaultOperationContext(new InMemoryIngestUnitOfWork(vaultRoot.Path), vaultRoot.Path);
        var handler = CreateHandler();

        var resolved = await handler.ResolveAsync(new AppendToNote("Scratch.md", "new appended line"), context);

        Assert.StartsWith(
            string.Join('\n', "---", "zebra: 1", "# a comment that must survive", "alpha: 2", "nested:", "  inner: value", "---") + "\n",
            resolved.Content);
        Assert.Contains("- existing bullet", resolved.Content);
        Assert.EndsWith("new appended line\n", resolved.Content);

        // Frontmatter block, verbatim, must be byte-identical to the original.
        var originalFrontmatter = MindBot.Core.Notes.NoteFrontmatterSplitter.Split(existing)!.Value.FrontmatterBlockVerbatim;
        Assert.StartsWith(originalFrontmatter, resolved.Content);
    }

    [Fact]
    public async Task ResolveAsync_MalformedFrontmatterYaml_ThrowsAndProducesNothing()
    {
        using var vaultRoot = new TempVaultRoot();
        var existing = string.Join('\n',
            "---",
            "this: [is, not, closed",
            "---",
            "",
            "body",
            "");
        vaultRoot.WriteFile("", "Scratch.md", existing);

        var context = new UnitOfWorkVaultOperationContext(new InMemoryIngestUnitOfWork(vaultRoot.Path), vaultRoot.Path);
        var handler = CreateHandler();

        await Assert.ThrowsAsync<VaultOperationException>(() =>
            handler.ResolveAsync(new AppendToNote("Scratch.md", "more"), context));
    }

    [Fact]
    public async Task ResolveAsync_NoRecognizableFrontmatter_KeepsWholeContentAsBody()
    {
        using var vaultRoot = new TempVaultRoot();
        const string existing = "just plain text, no frontmatter at all\n";
        vaultRoot.WriteFile("", "Scratch.md", existing);

        var context = new UnitOfWorkVaultOperationContext(new InMemoryIngestUnitOfWork(vaultRoot.Path), vaultRoot.Path);
        var handler = CreateHandler();

        var resolved = await handler.ResolveAsync(new AppendToNote("Scratch.md", "appended"), context);

        Assert.Contains("just plain text, no frontmatter at all", resolved.Content);
        Assert.EndsWith("appended\n", resolved.Content);
        Assert.StartsWith("---\n", resolved.Content);
    }

    [Fact]
    public async Task ResolveAsync_PathEscapingVaultRoot_Throws()
    {
        using var vaultRoot = new TempVaultRoot();
        var context = new UnitOfWorkVaultOperationContext(new InMemoryIngestUnitOfWork(vaultRoot.Path), vaultRoot.Path);
        var handler = CreateHandler();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.ResolveAsync(new AppendToNote("../../etc/passwd", "x"), context));
    }
}
