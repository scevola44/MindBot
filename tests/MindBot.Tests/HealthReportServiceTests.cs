using MindBot.Core.Durability;
using MindBot.Core.Git;
using MindBot.Core.Health;
using MindBot.Tests.Fakes;

namespace MindBot.Tests;

public class HealthReportServiceTests
{
    [Fact]
    public async Task BuildAsync_FreshSnapshot_ReportsHealthyAndNotDegraded()
    {
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.Zero));
        var snapshot = new HealthSnapshot();
        snapshot.RecordSuccessfulPoll(timeProvider.GetUtcNow());
        var service = new HealthReportService(snapshot, new InMemoryWriteJobQueue(), timeProvider);

        var payload = await service.BuildAsync();

        Assert.Equal("healthy", payload.Status);
        Assert.False(payload.Degraded);
        Assert.True(payload.StateStoreReachable);
        Assert.False(payload.PollStalled);
    }

    [Fact]
    public async Task BuildAsync_PollOlderThanThreshold_ReportsStalledAndUnhealthy()
    {
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.Zero));
        var snapshot = new HealthSnapshot();
        snapshot.RecordSuccessfulPoll(timeProvider.GetUtcNow());
        var service = new HealthReportService(snapshot, new InMemoryWriteJobQueue(), timeProvider);

        timeProvider.Now = timeProvider.Now.AddSeconds(200);

        var payload = await service.BuildAsync();

        Assert.True(payload.PollStalled);
        Assert.Equal("unhealthy", payload.Status);
    }

    [Fact]
    public async Task BuildAsync_UnpushedCommitsOrDirtyTree_ReportsDegraded_ButStillHealthy()
    {
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.Zero));
        var snapshot = new HealthSnapshot();
        snapshot.RecordSuccessfulPoll(timeProvider.GetUtcNow());
        snapshot.RecordGitStatus(new GitStatusSnapshot(WorkingTreeDirty: true, UnpushedCommitCount: 2));
        var service = new HealthReportService(snapshot, new InMemoryWriteJobQueue(), timeProvider);

        var payload = await service.BuildAsync();

        Assert.True(payload.Degraded);
        Assert.Equal("healthy", payload.Status);
        Assert.True(payload.WorkingTreeDirty);
        Assert.Equal(2, payload.UnpushedCommitCount);
    }

    [Fact]
    public async Task BuildAsync_QueueDepthReflectsPendingJobs()
    {
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.Zero));
        var queue = new InMemoryWriteJobQueue();
        queue.Enqueue("a.md", "content");
        queue.Enqueue("b.md", "content");
        var snapshot = new HealthSnapshot();
        snapshot.RecordSuccessfulPoll(timeProvider.GetUtcNow());
        snapshot.RecordQueueDepth(await queue.GetPendingCountAsync());
        var service = new HealthReportService(snapshot, queue, timeProvider);

        var payload = await service.BuildAsync();

        Assert.Equal(2, payload.QueueDepth);
    }
}
