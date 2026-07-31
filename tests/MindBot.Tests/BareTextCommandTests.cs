using MindBot.Core.Commands;
using MindBot.Core.Notes;
using MindBot.Core.Operations;
using MindBot.Tests.Fakes;

namespace MindBot.Tests;

public class BareTextCommandTests
{
    [Fact]
    public void Matches_AnyText_ReturnsTrue()
    {
        var command = new BareTextCommand(new FixedTimeProvider(DateTimeOffset.UnixEpoch));

        Assert.True(command.Matches("anything at all"));
        Assert.True(command.Matches("/even something slash-shaped"));
        Assert.True(command.Matches(string.Empty));
    }

    [Fact]
    public async Task HandleAsync_ProducesOneCreateNote_WithReplyEqualToFilename()
    {
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.Zero));
        var command = new BareTextCommand(timeProvider);
        using var vaultRoot = new TempVaultRoot();
        var context = new UnitOfWorkVaultOperationContext(new InMemoryIngestUnitOfWork(vaultRoot.Path), vaultRoot.Path);

        var result = await command.HandleAsync("Just a quick thought", context);

        var ops = Assert.IsType<CommandResult.Operations>(result);
        var op = Assert.IsType<CreateNote>(Assert.Single(ops.Items));
        Assert.Equal("05 - Fleeting/202607300900.md", op.Path);
        Assert.Equal("Just a quick thought", op.Body);
        Assert.Equal("202607300900.md", ops.Reply);
        Assert.Equal("Add note 202607300900.md", ops.CommitMessage);
    }

    [Fact]
    public async Task HandleAsync_CollidingFilenames_GetDistinctSuffixes()
    {
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.Zero));
        var command = new BareTextCommand(timeProvider);
        using var vaultRoot = new TempVaultRoot();
        var unitOfWork = new InMemoryIngestUnitOfWork(vaultRoot.Path);
        var context = new UnitOfWorkVaultOperationContext(unitOfWork, vaultRoot.Path);

        var firstResult = Assert.IsType<CommandResult.Operations>(await command.HandleAsync("one", context));
        var secondResult = Assert.IsType<CommandResult.Operations>(await command.HandleAsync("two", context));

        Assert.Equal("202607300900.md", firstResult.Reply);
        Assert.Equal("202607300900-2.md", secondResult.Reply);
    }

    [Fact]
    public async Task HandleAsync_HashtagsInMessage_AreAddedToFrontmatterTags()
    {
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.Zero));
        var command = new BareTextCommand(timeProvider);
        using var vaultRoot = new TempVaultRoot();
        var context = new UnitOfWorkVaultOperationContext(new InMemoryIngestUnitOfWork(vaultRoot.Path), vaultRoot.Path);

        var result = await command.HandleAsync("Plan the trip #travel and #budget", context);

        var ops = Assert.IsType<CommandResult.Operations>(result);
        var op = Assert.IsType<CreateNote>(Assert.Single(ops.Items));
        var frontmatter = Assert.IsType<NoteFrontmatter>(op.Frontmatter);
        Assert.Equal(["WIP", "MindBot", "travel", "budget"], frontmatter.Tags);
    }

    [Fact]
    public async Task HandleAsync_HashtagDuplicatingDefaultTag_IsNotAddedTwice()
    {
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.Zero));
        var command = new BareTextCommand(timeProvider);
        using var vaultRoot = new TempVaultRoot();
        var context = new UnitOfWorkVaultOperationContext(new InMemoryIngestUnitOfWork(vaultRoot.Path), vaultRoot.Path);

        var result = await command.HandleAsync("Still in progress #WIP", context);

        var ops = Assert.IsType<CommandResult.Operations>(result);
        var op = Assert.IsType<CreateNote>(Assert.Single(ops.Items));
        var frontmatter = Assert.IsType<NoteFrontmatter>(op.Frontmatter);
        Assert.Equal(["WIP", "MindBot"], frontmatter.Tags);
    }
}
