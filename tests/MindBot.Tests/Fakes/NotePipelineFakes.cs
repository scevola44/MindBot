using MindBot.Core.Git;
using MindBot.Core.Notes;

namespace MindBot.Tests.Fakes;

public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

public sealed class FakeGitService : IGitService
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

public sealed class FakeVaultWriter : IVaultWriter
{
    public List<(string Filename, string Content)> Written { get; } = [];

    public Exception? ThrowOnWrite { get; set; }

    public Task<string> WriteNoteAsync(string filename, string content, CancellationToken cancellationToken = default)
    {
        if (ThrowOnWrite is not null)
        {
            throw ThrowOnWrite;
        }

        Written.Add((filename, content));
        return Task.FromResult(filename);
    }
}
