using MindBot.Core.Commands;
using MindBot.Core.Health;
using MindBot.Core.Operations;
using MindBot.Tests.Fakes;

namespace MindBot.Tests;

public class StatusCommandTests
{
    [Theory]
    [InlineData("/status")]
    [InlineData("/STATUS")]
    [InlineData("/status@mybot")]
    public void Matches_StatusVariants_ReturnTrue(string text)
    {
        var service = new HealthReportService(new HealthSnapshot(), new InMemoryWriteJobQueue(), new FixedTimeProvider(DateTimeOffset.UnixEpoch));
        Assert.True(new StatusCommand(service).Matches(text));
    }

    [Fact]
    public void Matches_OtherText_ReturnsFalse()
    {
        var service = new HealthReportService(new HealthSnapshot(), new InMemoryWriteJobQueue(), new FixedTimeProvider(DateTimeOffset.UnixEpoch));
        Assert.False(new StatusCommand(service).Matches("status"));
        Assert.False(new StatusCommand(service).Matches("/statuses"));
    }

    [Fact]
    public async Task HandleAsync_ReturnsDirectReply_ReflectingHealthPayload()
    {
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.Zero));
        var snapshot = new HealthSnapshot();
        snapshot.RecordSuccessfulPoll(timeProvider.GetUtcNow());
        var service = new HealthReportService(snapshot, new InMemoryWriteJobQueue(), timeProvider);
        var command = new StatusCommand(service);
        using var vaultRoot = new TempVaultRoot();
        var context = new UnitOfWorkVaultOperationContext(new InMemoryIngestUnitOfWork(vaultRoot.Path), vaultRoot.Path);

        var result = await command.HandleAsync("/status", context);

        var direct = Assert.IsType<CommandResult.DirectReply>(result);
        Assert.Contains("Status: healthy", direct.Text);
        Assert.Contains("Queue depth: 0", direct.Text);
    }

    /// <summary>
    /// Structural, not behavioral: StatusCommand's whole dependency graph never references
    /// IGitService or IVaultWriter, so a git call or filesystem write from /status is not just
    /// untested -- it is impossible to reach from this command.
    /// </summary>
    [Fact]
    public void Constructor_DependencyGraph_HasNoGitOrFilesystemDependency()
    {
        var constructorParameterTypes = typeof(StatusCommand).GetConstructors().Single().GetParameters().Select(p => p.ParameterType);
        foreach (var type in constructorParameterTypes)
        {
            Assert.DoesNotContain("Git", type.Name);
            Assert.DoesNotContain("VaultWriter", type.Name);
        }
    }
}
