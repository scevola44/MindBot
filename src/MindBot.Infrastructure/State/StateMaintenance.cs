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

        // SQLite has no offset-aware date type, so its EF Core provider refuses to translate
        // relational comparisons (<, >) on DateTimeOffset columns at all - not just inside
        // ExecuteDelete/ExecuteUpdate. Whatever can be filtered in SQL (enum equality) is pushed
        // down; the DateTimeOffset cutoff itself is applied client-side after materializing.
        // These tables stay small precisely because pruning keeps them that way, so pulling the
        // (already narrowed) candidate rows into memory is cheap.
        var staleUpdates = (await db.ProcessedUpdates.ToListAsync(cancellationToken))
            .Where(u => u.ReceivedAt < updateCutoff)
            .ToList();
        db.ProcessedUpdates.RemoveRange(staleUpdates);

        var staleJobs = (await db.WriteJobs
                .Where(j => j.Status == WriteJobStatus.Completed)
                .ToListAsync(cancellationToken))
            .Where(j => j.EnqueuedAt < updateCutoff)
            .ToList();
        db.WriteJobs.RemoveRange(staleJobs);

        var staleConversations = (await db.Conversations.ToListAsync(cancellationToken))
            .Where(c => c.UpdatedAt < conversationCutoff)
            .ToList();
        db.Conversations.RemoveRange(staleConversations);

        var prunedCount = staleUpdates.Count + staleJobs.Count + staleConversations.Count;
        if (prunedCount > 0)
        {
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Pruned durability state: {Updates} processed update(s), {Jobs} completed job(s), {Conversations} expired conversation(s).",
                staleUpdates.Count,
                staleJobs.Count,
                staleConversations.Count);
        }
    }
}
