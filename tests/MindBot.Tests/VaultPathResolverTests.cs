using MindBot.Core.Notes;

namespace MindBot.Tests;

public class VaultPathResolverTests
{
    [Fact]
    public void ResolveNotePath_SimpleFilename_ResolvesInsideRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "mindbot-vault-a");
        var resolved = VaultPathResolver.ResolveNotePath(root, "2026-07-30T120000-note.md");

        Assert.Equal(Path.Combine(Path.GetFullPath(root), "2026-07-30T120000-note.md"), resolved);
    }

    [Fact]
    public void ResolveNotePath_TraversalFilename_Throws()
    {
        var root = Path.Combine(Path.GetTempPath(), "mindbot-vault-b");

        Assert.Throws<InvalidOperationException>(() =>
            VaultPathResolver.ResolveNotePath(root, "../outside.md"));
    }

    [Fact]
    public void ResolveNotePath_AbsolutePathEscape_Throws()
    {
        var root = Path.Combine(Path.GetTempPath(), "mindbot-vault-c");

        Assert.Throws<InvalidOperationException>(() =>
            VaultPathResolver.ResolveNotePath(root, "/etc/passwd"));
    }
}
