using MindBot.Core.Durability;
using MindBot.Core.Notes;
using MindBot.Core.Operations;
using MindBot.Core.Options;
using MindBot.Core.YouTube;
using MindBot.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MindBot.Tests;

public sealed class YouTubeSummaryJobRunnerTests
{
    private const string Payload = """{"videoId":"qIeJ7Gw9v_I","chunkCount":2}""";

    private static readonly DateTimeOffset Now = new(2026, 8, 5, 14, 32, 17, TimeSpan.Zero);

    private sealed class Harness : IDisposable
    {
        public TempVaultRoot VaultRoot { get; } = new();

        public InMemoryBackgroundJobQueue Queue { get; } = new();

        public FakeN8nClient N8n { get; } = new();

        public FakeVideoTitleResolver TitleResolver { get; } = new();

        public FakeChatReplySender Replies { get; } = new();

        public FixedTimeProvider Time { get; } = new(Now);

        public N8nOptions N8nOptions { get; } = new() { BaseUrl = "https://n8n.example/webhook", MaxAttempts = 3, RetryBaseSeconds = 30 };

        public FakeIngestUnitOfWorkFactory UnitOfWorkFactory { get; private set; } = null!;

        public YouTubeSummaryJobRunner CreateRunner()
        {
            UnitOfWorkFactory = new FakeIngestUnitOfWorkFactory(VaultRoot.Path, Queue.Complete);

            return new YouTubeSummaryJobRunner(
                Queue,
                new YouTubeSummaryPipeline(N8n, TitleResolver, NullLogger<YouTubeSummaryPipeline>.Instance),
                UnitOfWorkFactory,
                new VaultOperationApplier([new CreateNoteHandler()]),
                Replies,
                Options.Create(new VaultOptions { Root = VaultRoot.Path }),
                Options.Create(N8nOptions),
                Time,
                NullLogger<YouTubeSummaryJobRunner>.Instance);
        }

        public void Dispose() => VaultRoot.Dispose();
    }

    [Fact]
    public async Task AnEmptyQueueIsIdle()
    {
        using var harness = new Harness();

        Assert.Equal(BackgroundJobOutcome.Idle, await harness.CreateRunner().RunNextAsync());
    }

    [Fact]
    public async Task AJobStillBackingOffIsNotClaimed()
    {
        using var harness = new Harness();
        var job = harness.Queue.Enqueue(Payload);
        await harness.Queue.RecordFailureAsync(job.Id, "boom", Now.AddMinutes(5));

        Assert.Equal(BackgroundJobOutcome.Idle, await harness.CreateRunner().RunNextAsync());
        Assert.Empty(harness.N8n.Calls);
    }

    [Fact]
    public async Task QueuesTheNoteAndClosesTheJobInOneTransaction()
    {
        using var harness = new Harness();
        var job = harness.Queue.Enqueue(Payload);

        var outcome = await harness.CreateRunner().RunNextAsync();

        Assert.Equal(BackgroundJobOutcome.Completed, outcome);

        var unitOfWork = harness.UnitOfWorkFactory.Last;
        var write = Assert.Single(unitOfWork.Enqueued);
        Assert.Equal(VaultLayout.FleetingFolder, write.RelativeFolder);
        Assert.Equal("daggerheart-combat-is-broken.md", write.Filename);
        Assert.Equal(job.ChatId, write.ChatId);
        Assert.Equal(job.SenderId, write.SenderId);
        Assert.Equal(job.UpdateId, write.UpdateId);

        Assert.Equal([job.Id], unitOfWork.CompletedBackgroundJobs);
        Assert.True(unitOfWork.Committed);
        Assert.Equal(BackgroundJobStatus.Completed, harness.Queue[job.Id].Status);
    }

    [Fact]
    public async Task TheQueuedNoteIsTheFullySerializedYouTubeNote()
    {
        using var harness = new Harness();
        harness.Queue.Enqueue(Payload);

        await harness.CreateRunner().RunNextAsync();

        var content = harness.UnitOfWorkFactory.Last.Enqueued[0].Content;
        Assert.StartsWith("---\ntags:\n  - WIP\n  - Youtube\n  - AISummary\ndate: 2026-08-05T14:32\n", content);
        Assert.Contains("# Daggerheart combat is broken", content);
        Assert.Contains("*Source [YouTube](https://www.youtube.com/watch?v=qIeJ7Gw9v_I)*", content);
        Assert.Contains("```table-of-contents", content);
        Assert.Contains(harness.N8n.Summary, content);
    }

    [Fact]
    public async Task RepliesToTheChatThatAskedForIt()
    {
        using var harness = new Harness();
        var job = harness.Queue.Enqueue(Payload, chatId: 4242);

        await harness.CreateRunner().RunNextAsync();

        var reply = Assert.Single(harness.Replies.Sent);
        Assert.Equal(4242, reply.ChatId);
        Assert.Contains("daggerheart-combat-is-broken.md", reply.Text);
        Assert.Equal(BackgroundJobStatus.Completed, harness.Queue[job.Id].Status);
    }

    [Fact]
    public async Task ACompletedJobIsNotClaimedAgain()
    {
        using var harness = new Harness();
        harness.Queue.Enqueue(Payload);
        var runner = harness.CreateRunner();

        Assert.Equal(BackgroundJobOutcome.Completed, await runner.RunNextAsync());
        Assert.Equal(BackgroundJobOutcome.Idle, await runner.RunNextAsync());
    }

    [Fact]
    public async Task AFailedAttemptSchedulesAnExponentialRetryAndQueuesNothing()
    {
        using var harness = new Harness();
        harness.N8n.FailOnCall = "summarize-chunks";
        var job = harness.Queue.Enqueue(Payload);

        var outcome = await harness.CreateRunner().RunNextAsync();

        Assert.Equal(BackgroundJobOutcome.Retrying, outcome);

        var stored = harness.Queue[job.Id];
        Assert.Equal(BackgroundJobStatus.Pending, stored.Status);
        Assert.Equal(1, stored.Attempts);
        Assert.Equal(Now.AddSeconds(30), stored.NextAttemptAt);
        Assert.Contains("summarize-chunks", stored.LastError);
        Assert.Empty(harness.UnitOfWorkFactory.Created);
        Assert.Empty(harness.Replies.Sent);
    }

    [Fact]
    public async Task TheBackoffDoublesWithEachAttempt()
    {
        using var harness = new Harness();
        harness.N8n.FailOnCall = "get-yt-transcript";
        var job = harness.Queue.Enqueue(Payload);
        var runner = harness.CreateRunner();

        await runner.RunNextAsync();
        Assert.Equal(Now.AddSeconds(30), harness.Queue[job.Id].NextAttemptAt);

        // Let the backoff elapse so the second attempt is claimable.
        harness.Time.Now = Now.AddMinutes(1);
        await runner.RunNextAsync();
        Assert.Equal(Now.AddMinutes(1).AddSeconds(60), harness.Queue[job.Id].NextAttemptAt);
    }

    [Fact]
    public async Task TheLastAttemptGivesUpAndTellsTheUser()
    {
        using var harness = new Harness();
        harness.N8nOptions.MaxAttempts = 2;
        harness.N8n.FailOnCall = "chunks-reducer";
        var job = harness.Queue.Enqueue(Payload);
        var runner = harness.CreateRunner();

        Assert.Equal(BackgroundJobOutcome.Retrying, await runner.RunNextAsync());

        harness.Time.Now = Now.AddMinutes(5);
        Assert.Equal(BackgroundJobOutcome.Failed, await runner.RunNextAsync());

        Assert.Equal(BackgroundJobStatus.Failed, harness.Queue[job.Id].Status);
        Assert.Contains("Could not summarise", Assert.Single(harness.Replies.Sent).Text);

        // Permanently failed, so the worker never picks it up again.
        harness.Time.Now = Now.AddDays(1);
        Assert.Equal(BackgroundJobOutcome.Idle, await runner.RunNextAsync());
    }

    /// <summary>An unreadable payload cannot become readable, so retrying it would just burn attempts.</summary>
    [Fact]
    public async Task AnUnreadablePayloadFailsImmediatelyWithoutCallingN8n()
    {
        using var harness = new Harness();
        var job = harness.Queue.Enqueue("not json at all");

        Assert.Equal(BackgroundJobOutcome.Failed, await harness.CreateRunner().RunNextAsync());

        Assert.Equal(BackgroundJobStatus.Failed, harness.Queue[job.Id].Status);
        Assert.Empty(harness.N8n.Calls);
        Assert.Contains("could not be read", Assert.Single(harness.Replies.Sent).Text);
    }

    [Fact]
    public async Task IgnoresJobsOfAnotherKind()
    {
        using var harness = new Harness();
        harness.Queue.Enqueue(Payload, kind: "something-else");

        Assert.Equal(BackgroundJobOutcome.Idle, await harness.CreateRunner().RunNextAsync());
    }

    /// <summary>Shutdown must leave the job pending so the next start re-runs it, not consume an attempt.</summary>
    [Fact]
    public async Task CancellationDuringThePipelineLeavesTheJobUntouched()
    {
        using var harness = new Harness();
        var job = harness.Queue.Enqueue(Payload);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            harness.CreateRunner().RunNextAsync(cancellation.Token));

        var stored = harness.Queue[job.Id];
        Assert.Equal(BackgroundJobStatus.Pending, stored.Status);
        Assert.Equal(0, stored.Attempts);
    }
}
