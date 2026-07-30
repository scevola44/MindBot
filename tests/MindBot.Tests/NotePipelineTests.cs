using MindBot.Core.Git;
using MindBot.Core.Notes;
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

        Assert.Equal("2026-07-30T090000-hello-vault.md", result.Filename);
        Assert.Equal(1, git.PullCalls);
        Assert.Equal(1, git.CommitCalls);
        Assert.Equal(1, git.PushCalls);
        Assert.Single(vault.Written);
        Assert.Equal("2026-07-30T090000-hello-vault.md", vault.Written[0].Filename);
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class FakeGitService : IGitService
    {
        public GitOperationResult PullResult { get; set; } = GitOperationResult.Ok;
        public GitOperationResult CommitResult { get; set; } = GitOperationResult.Ok;
        public GitOperationResult PushResult { get; set; } = GitOperationResult.Ok;

        public int PullCalls { get; private set; }
        public int CommitCalls { get; private set; }
        public int PushCalls { get; private set; }

        public Task<GitOperationResult> EnsureRepositoryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(GitOperationResult.Ok);

        public Task<GitOperationResult> PullAsync(CancellationToken cancellationToken = default)
        {
            PullCalls++;
            return Task.FromResult(PullResult);
        }

        public Task<GitOperationResult> CommitAsync(string message, CancellationToken cancellationToken = default)
        {
            CommitCalls++;
            return Task.FromResult(CommitResult);
        }

        public Task<GitOperationResult> PushAsync(CancellationToken cancellationToken = default)
        {
            PushCalls++;
            return Task.FromResult(PushResult);
        }

        public Task<GitOperationResult> VerifyRemoteWritableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(GitOperationResult.Ok);
    }

    private sealed class FakeVaultWriter : IVaultWriter
    {
        public List<(string Filename, string Content)> Written { get; } = [];

        public Task<string> WriteNoteAsync(string filename, string content, CancellationToken cancellationToken = default)
        {
            Written.Add((filename, content));
            return Task.FromResult(filename);
        }
    }
}
