using MindBot.Core.Durability;
using MindBot.Core.Git;
using MindBot.Core.Notes;
using MindBot.Core.Notifications;

namespace MindBot.Tests.Fakes;

public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

public sealed class FakeGitService : IGitService
{
    public GitClassification ClassificationResult { get; set; } = new(GitSyncStrategy.FastForward, 0, false);

    public GitOperationResult CommitResult { get; set; } = GitOperationResult.Ok;

    /// <summary>Consumed one per push; the last entry repeats once exhausted.</summary>
    public List<GitPushResult> PushResults { get; } = [GitPushResult.Ok];

    public GitStatusSnapshot Status { get; set; } = new(false, 0);

    public string HeadSha { get; set; } = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    public List<string> CommitMessages { get; } = [];

    public List<string?> SynchronizeCalls { get; } = [];

    public int PushCalls { get; private set; }

    public Task<GitOperationResult> EnsureRepositoryAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(GitOperationResult.Ok);

    public Task<GitClassification> SynchronizeAsync(string? lastPushedSha, CancellationToken cancellationToken = default)
    {
        SynchronizeCalls.Add(lastPushedSha);
        return Task.FromResult(ClassificationResult);
    }

    public Task<GitOperationResult> CommitAsync(string message, CancellationToken cancellationToken = default)
    {
        CommitMessages.Add(message);
        return Task.FromResult(CommitResult);
    }

    public Task<GitPushResult> PushAsync(CancellationToken cancellationToken = default)
    {
        var index = Math.Min(PushCalls, PushResults.Count - 1);
        PushCalls++;
        return Task.FromResult(PushResults[index]);
    }

    public Task<GitStatusSnapshot> GetStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Status);

    public Task<string?> GetHeadShaAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(HeadSha);

    public Task<GitOperationResult> VerifyRemoteWritableAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(GitOperationResult.Ok);
}

public sealed class FakeVaultWriter(string? vaultRoot = null) : IVaultWriter
{
    public List<(string RelativeFolder, string Filename, string Content)> Written { get; } = [];

    public Exception? ThrowOnWrite { get; set; }

    public Task<string> WriteNoteAsync(string relativeFolder, string filename, string content, CancellationToken cancellationToken = default)
    {
        if (ThrowOnWrite is not null)
        {
            throw ThrowOnWrite;
        }

        Written.Add((relativeFolder, filename, content));

        if (vaultRoot is not null)
        {
            var path = Path.Combine(vaultRoot, relativeFolder, filename);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        return Task.FromResult(filename);
    }
}

public sealed class FakeOperatorNotifier : IOperatorNotifier
{
    public List<string> Messages { get; } = [];

    public List<string> RaisedKeys { get; } = [];

    public List<string> ClearedKeys { get; } = [];

    private readonly HashSet<string> _latches = [];

    public Task NotifyAsync(string message, CancellationToken cancellationToken = default)
    {
        Messages.Add(message);
        return Task.CompletedTask;
    }

    public Task NotifyOnceAsync(string key, string message, CancellationToken cancellationToken = default)
    {
        if (_latches.Add(key))
        {
            RaisedKeys.Add(key);
            Messages.Add(message);
        }

        return Task.CompletedTask;
    }

    public Task ClearAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_latches.Remove(key))
        {
            ClearedKeys.Add(key);
        }

        return Task.CompletedTask;
    }
}

public sealed class InMemoryWriteJobQueue : IWriteJobQueue
{
    private readonly List<WriteJob> _jobs = [];
    private long _nextId = 1;

    public IReadOnlyList<WriteJob> All => _jobs;

    public WriteJob Enqueue(
        string filename,
        string content,
        long updateId = 1,
        long chatId = 42,
        long senderId = 7,
        string relativeFolder = VaultLayout.FleetingFolder)
    {
        var job = new WriteJob(_nextId++, updateId, relativeFolder, filename, content, chatId, senderId, DateTimeOffset.UnixEpoch, WriteJobStatus.Pending);
        _jobs.Add(job);
        return job;
    }

    public Task<IReadOnlyList<WriteJob>> GetPendingAsync(int maxCount, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WriteJob>>(
            _jobs.Where(j => j.Status == WriteJobStatus.Pending).OrderBy(j => j.Id).Take(maxCount).ToList());

    public Task<int> GetPendingCountAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_jobs.Count(j => j.Status == WriteJobStatus.Pending));

    public Task MarkCompletedAsync(IReadOnlyCollection<long> jobIds, CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < _jobs.Count; i++)
        {
            if (jobIds.Contains(_jobs[i].Id))
            {
                _jobs[i] = _jobs[i] with { Status = WriteJobStatus.Completed };
            }
        }

        return Task.CompletedTask;
    }
}

public sealed class InMemoryRepositoryStateStore(string? lastPushedSha = null) : IRepositoryStateStore
{
    public string? LastPushedSha { get; private set; } = lastPushedSha;

    public DateTimeOffset? LastSuccessfulPushAt { get; private set; }

    public Task<RepositoryState> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new RepositoryState(LastPushedSha, 0, LastSuccessfulPushAt));

    public Task SetLastPushedShaAsync(string sha, DateTimeOffset pushedAt, CancellationToken cancellationToken = default)
    {
        LastPushedSha = sha;
        LastSuccessfulPushAt = pushedAt;
        return Task.CompletedTask;
    }
}

/// <summary>
/// In-memory stand-in for the SQLite ingest transaction, for tests about routing rather than
/// persistence. Filename reservation mirrors the real implementation: existing files on disk and
/// filenames already claimed by pending jobs both push the candidate along.
/// </summary>
public sealed class InMemoryIngestUnitOfWork(string? vaultRoot = null, Action<long>? onBackgroundJobCompleted = null) : IIngestUnitOfWork
{
    private readonly Dictionary<long, ConversationState> _conversations = [];
    private readonly HashSet<long> _processedUpdates = [];
    private readonly HashSet<string> _reserved = [];
    private readonly Dictionary<(string Folder, string Filename), string> _latestContent = [];

    public List<(long UpdateId, string RelativeFolder, string Filename, string Content, long ChatId, long SenderId)> Enqueued { get; } = [];

    public List<(long UpdateId, string Kind, string Payload, long ChatId, long SenderId)> EnqueuedBackgroundJobs { get; } = [];

    public List<long> CompletedBackgroundJobs { get; } = [];

    public bool Committed { get; private set; }

    public int? TelegramOffset { get; private set; }

    public void SeedProcessed(long updateId) => _processedUpdates.Add(updateId);

    public ConversationState Conversation(long chatId) =>
        _conversations.TryGetValue(chatId, out var state) ? state : ConversationState.None;

    public Task<bool> IsUpdateProcessedAsync(long updateId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_processedUpdates.Contains(updateId));

    public Task<ConversationState> GetConversationAsync(long chatId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Conversation(chatId));

    public Task SetConversationAsync(long chatId, ConversationState state, CancellationToken cancellationToken = default)
    {
        _conversations[chatId] = state;
        return Task.CompletedTask;
    }

    public Task ClearConversationAsync(long chatId, CancellationToken cancellationToken = default)
    {
        _conversations.Remove(chatId);
        return Task.CompletedTask;
    }

    public Task<string> ReserveFilenameAsync(string baseFilename, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            var candidate = NoteFilenameFactory.CreateCandidate(baseFilename, attempt);

            if (vaultRoot is not null && File.Exists(Path.Combine(vaultRoot, VaultLayout.RelativeNotePath(candidate))))
            {
                continue;
            }

            if (_reserved.Add(candidate))
            {
                return Task.FromResult(candidate);
            }
        }
    }

    public Task<string?> GetLatestNoteContentAsync(string relativeFolder, string filename, CancellationToken cancellationToken = default)
    {
        if (_latestContent.TryGetValue((relativeFolder, filename), out var content))
        {
            return Task.FromResult<string?>(content);
        }

        if (vaultRoot is not null)
        {
            var path = Path.Combine(vaultRoot, relativeFolder, filename);
            if (File.Exists(path))
            {
                return Task.FromResult<string?>(File.ReadAllText(path));
            }
        }

        return Task.FromResult<string?>(null);
    }

    public Task EnqueueWriteJobAsync(
        long updateId,
        string relativeFolder,
        string filename,
        string content,
        long chatId,
        long senderId,
        CancellationToken cancellationToken = default)
    {
        Enqueued.Add((updateId, relativeFolder, filename, content, chatId, senderId));
        _latestContent[(relativeFolder, filename)] = content;
        return Task.CompletedTask;
    }

    public Task EnqueueBackgroundJobAsync(
        long updateId,
        string kind,
        string payload,
        long chatId,
        long senderId,
        CancellationToken cancellationToken = default)
    {
        EnqueuedBackgroundJobs.Add((updateId, kind, payload, chatId, senderId));
        return Task.CompletedTask;
    }

    public Task CompleteBackgroundJobAsync(long jobId, CancellationToken cancellationToken = default)
    {
        CompletedBackgroundJobs.Add(jobId);
        onBackgroundJobCompleted?.Invoke(jobId);
        return Task.CompletedTask;
    }

    public Task MarkUpdateProcessedAsync(long updateId, CancellationToken cancellationToken = default)
    {
        _processedUpdates.Add(updateId);
        return Task.CompletedTask;
    }

    public Task SetTelegramOffsetAsync(int offset, CancellationToken cancellationToken = default)
    {
        TelegramOffset = offset;
        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        Committed = true;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
