using MindBot.Core.Health;
using MindBot.Core.Operations;

namespace MindBot.Core.Commands;

/// <summary>
/// DirectReply containing the same information already exposed by the /health endpoint. This
/// command's dependency graph (<see cref="HealthReportService"/> -&gt; HealthSnapshot,
/// IWriteJobQueue, TimeProvider) contains no IGitService/IVaultWriter anywhere, so a git call or
/// filesystem write from /status is structurally impossible, not merely untested.
/// </summary>
public sealed class StatusCommand(HealthReportService healthReportService) : ICommand
{
    public bool Matches(string messageText) => string.Equals(CommandText.ExtractCommand(messageText), "/status", StringComparison.OrdinalIgnoreCase);

    public async Task<CommandResult> HandleAsync(string messageText, IVaultOperationContext context, CancellationToken cancellationToken = default)
    {
        var payload = await healthReportService.BuildAsync(cancellationToken);

        var text = $"""
            Status: {payload.Status}{(payload.Degraded ? " (degraded)" : "")}
            Last poll: {Format(payload.LastSuccessfulPollUtc)}
            Last push: {Format(payload.LastSuccessfulPushUtc)}
            Queue depth: {payload.QueueDepth}
            Unpushed commits: {payload.UnpushedCommitCount}
            Working tree dirty: {payload.WorkingTreeDirty}
            Last classification: {payload.LastClassification ?? "none"}
            State store reachable: {payload.StateStoreReachable}
            Poll stalled: {payload.PollStalled}
            """;

        return new CommandResult.DirectReply(text);
    }

    private static string Format(DateTimeOffset? value) => value?.ToString("u") ?? "never";
}
