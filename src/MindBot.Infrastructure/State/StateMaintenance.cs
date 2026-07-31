using MindBot.Core.Durability;
using MindBot.Core.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MindBot.Infrastructure.State;

/// <summary>
/// Keeps the durability tables from growing without bound. Processed update IDs only need to
/// outlive Telegram's own ~24h redelivery window, and completed write jobs have no purpose once
/// their note is in git history.
/// </summary>
public sealed class StateMaintenance(
    IDbContextFactory<MindBotDbContext> dbContextFactory,
    IOptions<StateOptions> stateOptions,
    TimeProvider timeProvider,
    ILogger<StateMaintenance> logger)
{
    private readonly StateOptions _stateOptions = stateOptions.Value;

    public async Task PruneAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();
        var updateCutoff = now - TimeSpan.FromDays(_stateOptions.ProcessedUpdateRetentionDays);
        var conversationCutoff = now - TimeSpan.FromMinutes(_stateOptions.ConversationExpiryMinutes);

        var prunedUpdates = await db.ProcessedUpdates
            .Where(u => u.ReceivedAt < updateCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        var prunedJobs = await db.WriteJobs
            .Where(j => j.Status == WriteJobStatus.Completed && j.EnqueuedAt < updateCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        var prunedConversations = await db.Conversations
            .Where(c => c.UpdatedAt < conversationCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        if (prunedUpdates + prunedJobs + prunedConversations > 0)
        {
            logger.LogInformation(
                "Pruned durability state: {Updates} processed update(s), {Jobs} completed job(s), {Conversations} expired conversation(s).",
                prunedUpdates,
                prunedJobs,
                prunedConversations);
        }
    }
}
