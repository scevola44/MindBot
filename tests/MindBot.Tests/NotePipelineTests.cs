using MindBot.Core.Git;
using MindBot.Core.Notes;
using MindBot.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace MindBot.Tests;

public class NotePipelineTests
{
    [Fact]
    public async Task CreateNoteAsync_WritesCommitsAndPushes_ReturnsFilename()
    {
        var git = new FakeGitService();
        var vault = new FakeVaultWriter();
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.Zero));
        var pipeline = new NotePipeline(git, vault, time, NullLogger<NotePipeline>.Instance);

        var result = await pipeline.CreateNoteAsync("Hello vault");

        Assert.Equal("202607300900.md", result.Filename);
        Assert.Equal(1, git.PullCalls);
        Assert.Equal(1, git.CommitCalls);
        Assert.Equal(1, git.PushCalls);
        Assert.Single(vault.Written);
        Assert.Equal("202607300900.md", vault.Written[0].Filename);
    }

    [Fact]
    public async Task CreateNoteAsync_PullFails_StillWritesAndCommits()
    {
        var git = new FakeGitService { PullResult = GitOperationResult.Fail("remote unreachable") };
        var vault = new FakeVaultWriter();
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.Zero));
        var pipeline = new NotePipeline(git, vault, time, NullLogger<NotePipeline>.Instance);

        var result = await pipeline.CreateNoteAsync("Still works offline");

        Assert.Single(vault.Written);
        Assert.Equal(1, git.CommitCalls);
        Assert.NotNull(result.Filename);
    }

    [Fact]
    public async Task CreateNamedNoteAsync_WritesCommitsAndPushes_ReturnsSanitizedFilename()
    {
        var git = new FakeGitService();
        var vault = new FakeVaultWriter();
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.Zero));
        var pipeline = new NotePipeline(git, vault, time, NullLogger<NotePipeline>.Instance);

        var result = await pipeline.CreateNamedNoteAsync("My Great Note!", "The content");

        Assert.Equal("my-great-note.md", result.Filename);
        Assert.Equal(1, git.PullCalls);
        Assert.Equal(1, git.CommitCalls);
        Assert.Equal(1, git.PushCalls);
        Assert.Single(vault.Written);
        Assert.Equal("my-great-note.md", vault.Written[0].Filename);
        Assert.Contains("The content", vault.Written[0].Content);
    }

    [Fact]
    public async Task CreateNamedNoteAsync_PullFails_StillWritesAndCommits()
    {
        var git = new FakeGitService { PullResult = GitOperationResult.Fail("remote unreachable") };
        var vault = new FakeVaultWriter();
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.Zero));
        var pipeline = new NotePipeline(git, vault, time, NullLogger<NotePipeline>.Instance);

        var result = await pipeline.CreateNamedNoteAsync("Groceries", "Milk and eggs");

        Assert.Single(vault.Written);
        Assert.Equal(1, git.CommitCalls);
        Assert.Equal("groceries.md", result.Filename);
    }
}
