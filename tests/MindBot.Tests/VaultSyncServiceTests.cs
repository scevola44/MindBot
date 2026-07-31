using MindBot.Core.Durability;
using MindBot.Core.Git;
using MindBot.Core.Health;
using MindBot.Core.Notifications;
using MindBot.Core.Options;
using MindBot.Core.Sync;
using MindBot.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MindBot.Tests;

public class VaultSyncServiceTests
{
    private static (VaultSyncService Sync, FakeGitService Git, FakeVaultWriter Vault, InMemoryWriteJobQueue Queue,
        InMemoryRepositoryStateStore State, FakeOperatorNotifier Notifier, HealthSnapshot Health) Create(
        GitOptions? gitOptions = null,
        string? lastPushedSha = null)
    {
        var git = new FakeGitService();
        var vault = new FakeVaultWriter();
        var queue = new InMemoryWriteJobQueue();
        var state = new InMemoryRepositoryStateStore(lastPushedSha);
        var notifier = new FakeOperatorNotifier();
        var health = new HealthSnapshot();

        var options = gitOptions ?? new GitOptions
        {
            Branch = "bot-inbox",
            MaxBatchSize = 100,
            PushRetryCount = 1,
            PushRetryBaseSeconds = 1,
        };

        var sync = new VaultSyncService(
            git,
            vault,
            queue,
            state,
            notifier,
            health,
            new FixedTimeProvider(new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero)),
            Options.Create(options),
            NullLogger<VaultSyncService>.Instance);

        return (sync, git, vault, queue, state, notifier, health);
    }

    [Fact]
    public async Task DrainOnceAsync_TenQueuedJobs_WritesTenNotesInOneCommit()
    {
        var (sync, git, vault, queue, _, _, _) = Create();

        for (var i = 0; i < 10; i++)
        {
            queue.Enqueue($"20260731120{i}.md", $"note {i}", updateId: i);
        }

        var result = await sync.DrainOnceAsync();

        Assert.Equal(DrainResult.Pushed, result);
        Assert.Equal(10, vault.Written.Count);
        Assert.Single(git.CommitMessages);
        Assert.Equal("Add 10 notes", git.CommitMessages[0]);
        Assert.Equal(1, git.PushCalls);
        Assert.All(queue.All, job => Assert.Equal(WriteJobStatus.Completed, job.Status));
    }

    [Fact]
    public async Task DrainOnceAsync_SingleJob_UsesFilenameInCommitMessage()
    {
        var (sync, git, _, queue, _, _, _) = Create();
        queue.Enqueue("202607311200.md", "hello");

        await sync.DrainOnceAsync();

        Assert.Equal("Add note 202607311200.md", git.CommitMessages[0]);
    }

    [Fact]
    public async Task DrainOnceAsync_EmptyQueue_IsIdleAndDoesNotCommit()
    {
        var (sync, git, _, _, _, _, _) = Create();

        var result = await sync.DrainOnceAsync();

        Assert.Equal(DrainResult.Idle, result);
        Assert.Empty(git.CommitMessages);
    }

    [Fact]
    public async Task DrainOnceAsync_SuccessfulPush_RecordsLastPushedSha()
    {
        var (sync, git, _, queue, state, _, _) = Create();
        git.HeadSha = "1234567890abcdef1234567890abcdef12345678";
        queue.Enqueue("202607311200.md", "hello");

        await sync.DrainOnceAsync();

        Assert.Equal("1234567890abcdef1234567890abcdef12345678", state.LastPushedSha);
        Assert.NotNull(state.LastSuccessfulPushAt);
    }

    [Fact]
    public async Task DrainOnceAsync_PassesLastPushedShaToClassification()
    {
        var (sync, git, _, queue, _, _, _) = Create(lastPushedSha: "deadbeef");
        queue.Enqueue("202607311200.md", "hello");

        await sync.DrainOnceAsync();

        Assert.Equal("deadbeef", git.SynchronizeCalls[0]);
    }

    [Fact]
    public async Task DrainOnceAsync_CommitFails_LeavesJobsPendingForRetry()
    {
        var (sync, git, _, queue, _, _, _) = Create();
        git.CommitResult = GitOperationResult.Fail("index.lock exists");
        queue.Enqueue("202607311200.md", "hello");

        var result = await sync.DrainOnceAsync();

        Assert.Equal(DrainResult.CommitFailed, result);
        Assert.All(queue.All, job => Assert.Equal(WriteJobStatus.Pending, job.Status));
    }

    [Fact]
    public async Task DrainOnceAsync_PushFails_StillCompletesJobs_AndReportsDegraded()
    {
        var (sync, git, _, queue, _, notifier, _) = Create();
        git.PushResults.Clear();
        git.PushResults.Add(GitPushResult.NetworkError("could not resolve host"));
        git.Status = new GitStatusSnapshot(false, 1);
        queue.Enqueue("202607311200.md", "hello");

        var result = await sync.DrainOnceAsync();

        // The commit is the durability boundary: the note is safe in git history even though it
        // has not reached the remote, so the job must not stay queued forever.
        Assert.Equal(DrainResult.CommittedNotPushed, result);
        Assert.All(queue.All, job => Assert.Equal(WriteJobStatus.Completed, job.Status));
        Assert.Contains(OperatorAlertKeys.PushFailing, notifier.RaisedKeys);
    }

    [Fact]
    public async Task DrainOnceAsync_PersistentPushFailure_AlertsOperatorOnlyOnce()
    {
        var (sync, git, _, queue, _, notifier, _) = Create();
        git.PushResults.Clear();
        git.PushResults.Add(GitPushResult.NetworkError("could not resolve host"));

        queue.Enqueue("202607311200.md", "first");
        await sync.DrainOnceAsync();

        queue.Enqueue("202607311201.md", "second");
        await sync.DrainOnceAsync();

        Assert.Single(notifier.RaisedKeys);
    }

    [Fact]
    public async Task DrainOnceAsync_PushRecoversAfterFailure_ClearsTheAlertLatch()
    {
        var (sync, git, _, queue, _, notifier, _) = Create();
        git.PushResults.Clear();
        git.PushResults.Add(GitPushResult.NetworkError("could not resolve host"));

        queue.Enqueue("202607311200.md", "first");
        await sync.DrainOnceAsync();
        Assert.Contains(OperatorAlertKeys.PushFailing, notifier.RaisedKeys);

        git.PushResults.Clear();
        git.PushResults.Add(GitPushResult.Ok);
        queue.Enqueue("202607311201.md", "second");
        await sync.DrainOnceAsync();

        Assert.Contains(OperatorAlertKeys.PushFailing, notifier.ClearedKeys);
    }

    [Fact]
    public async Task DrainOnceAsync_RejectedPushThenSuccess_ReclassifiesBeforeRetrying()
    {
        var options = new GitOptions
        {
            Branch = "bot-inbox",
            MaxBatchSize = 100,
            PushRetryCount = 2,
            PushRetryBaseSeconds = 1,
        };
        var (sync, git, _, queue, _, _, _) = Create(options);

        git.PushResults.Clear();
        git.PushResults.Add(GitPushResult.Rejected("non-fast-forward"));
        git.PushResults.Add(GitPushResult.Ok);
        queue.Enqueue("202607311200.md", "hello");

        var result = await sync.DrainOnceAsync();

        Assert.Equal(DrainResult.Pushed, result);
        Assert.Equal(2, git.PushCalls);
        // Once before writing the batch, once again after the rejection.
        Assert.Equal(2, git.SynchronizeCalls.Count);
    }

    [Fact]
    public async Task DrainOnceAsync_BranchRewrittenDuringRetry_ReappliesTheBatchOnTopOfTheNewOrigin()
    {
        var options = new GitOptions
        {
            Branch = "bot-inbox",
            MaxBatchSize = 100,
            PushRetryCount = 2,
            PushRetryBaseSeconds = 1,
        };
        var (sync, git, vault, queue, _, notifier, _) = Create(options);

        git.PushResults.Clear();
        git.PushResults.Add(GitPushResult.Rejected("non-fast-forward"));
        git.PushResults.Add(GitPushResult.Ok);
        git.ClassificationResult = new GitClassification(
            GitSyncStrategy.RemoteRewritten, 2, false, "/data/recovery/bot-inbox.bundle", 2);

        queue.Enqueue("202607311200.md", "just captured");

        await sync.DrainOnceAsync();

        // The reset discarded the commit this batch had just made. These notes arrived seconds
        // ago and were never pushed, so they cannot be ones the operator triaged — they get
        // re-applied rather than being left only in the bundle.
        Assert.Equal(2, vault.Written.Count);
        Assert.All(vault.Written, w => Assert.Equal("202607311200.md", w.Filename));
        Assert.Equal(2, git.CommitMessages.Count);
        Assert.Contains(notifier.Messages, m => m.Contains("bot-inbox.bundle"));
    }

    [Fact]
    public async Task DrainOnceAsync_ReplayedAfterACrash_LeavesExactlyOneNote()
    {
        // A crash between writing the note and marking the job complete replays the job on the
        // next start. Because the filename and content were fixed at ingest, the replay rewrites
        // the same path with the same bytes rather than allocating a second note.
        var vaultRoot = Path.Combine(Path.GetTempPath(), "mindbot-replay-" + Guid.NewGuid());
        Directory.CreateDirectory(vaultRoot);

        try
        {
            const string filename = "202607311200.md";
            const string content = "exactly these bytes";

            for (var run = 0; run < 2; run++)
            {
                var queue = new InMemoryWriteJobQueue();
                queue.Enqueue(filename, content);

                var sync = new VaultSyncService(
                    new FakeGitService(),
                    new FakeVaultWriter(vaultRoot),
                    queue,
                    new InMemoryRepositoryStateStore(),
                    new FakeOperatorNotifier(),
                    new HealthSnapshot(),
                    new FixedTimeProvider(new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero)),
                    Options.Create(new GitOptions { Branch = "bot-inbox", MaxBatchSize = 100, PushRetryCount = 1, PushRetryBaseSeconds = 1 }),
                    NullLogger<VaultSyncService>.Instance);

                await sync.DrainOnceAsync();
            }

            var notes = Directory.GetFiles(Path.Combine(vaultRoot, "05 - Fleeting"));
            Assert.Single(notes);
            Assert.Equal(filename, Path.GetFileName(notes[0]));
            Assert.Equal(content, await File.ReadAllTextAsync(notes[0]));
        }
        finally
        {
            Directory.Delete(vaultRoot, recursive: true);
        }
    }

    [Fact]
    public async Task DrainOnceAsync_RemoteRewritten_NotifiesOperatorWithBundlePathAndCount()
    {
        var (sync, git, _, queue, _, notifier, _) = Create();
        git.ClassificationResult = new GitClassification(
            GitSyncStrategy.RemoteRewritten, 3, false, "/data/recovery/bot-inbox-20260731T120000Z.bundle", 3);
        queue.Enqueue("202607311200.md", "hello");

        await sync.DrainOnceAsync();

        var alert = Assert.Single(notifier.Messages, m => m.Contains("reset or rewritten"));
        Assert.Contains("/data/recovery/bot-inbox-20260731T120000Z.bundle", alert);
        Assert.Contains("3", alert);
    }

    [Fact]
    public async Task DrainOnceAsync_BundleCouldNotBeWritten_AlertsAndKeepsCommits()
    {
        var (sync, git, _, queue, _, notifier, _) = Create();
        git.ClassificationResult = new GitClassification(
            GitSyncStrategy.RemoteRewritten, 2, false, RecoveryBundlePath: null, RecoveredCommitCount: 0,
            Error: "disk full");
        queue.Enqueue("202607311200.md", "hello");

        await sync.DrainOnceAsync();

        Assert.Contains(notifier.Messages, m => m.Contains("could not write a recovery bundle"));
    }

    [Fact]
    public async Task RefreshAndRetryPushAsync_NoUnpushedCommits_DoesNotPush()
    {
        var (sync, git, _, _, _, _, _) = Create();
        git.Status = new GitStatusSnapshot(false, 0);

        await sync.RefreshAndRetryPushAsync();

        Assert.Equal(0, git.PushCalls);
    }

    [Fact]
    public async Task RefreshAndRetryPushAsync_UnpushedCommits_RetriesThePush()
    {
        var (sync, git, _, _, _, _, _) = Create();
        git.Status = new GitStatusSnapshot(false, 2);

        await sync.RefreshAndRetryPushAsync();

        Assert.Equal(1, git.PushCalls);
    }

    [Fact]
    public async Task RefreshStatusAsync_UpdatesTheHealthSnapshot()
    {
        var (sync, git, _, queue, _, _, health) = Create();
        git.Status = new GitStatusSnapshot(true, 4);
        queue.Enqueue("202607311200.md", "hello");

        await sync.RefreshStatusAsync();

        var report = health.Read();
        Assert.True(report.WorkingTreeDirty);
        Assert.Equal(4, report.UnpushedCommitCount);
        Assert.Equal(1, report.QueueDepth);
        Assert.True(report.Degraded);
    }
}
