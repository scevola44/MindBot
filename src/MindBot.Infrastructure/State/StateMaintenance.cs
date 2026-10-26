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
    IOptions<WebOptions> webOptions,
    TimeProvider timeProvider,
    ILogger<StateMaintenance> logger)
{
    private readonly StateOptions _stateOptions = stateOptions.Value;
    private readonly WebOptions _webOptions = webOptions.Value;

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

        // Failed jobs are pruned alongside completed ones: the failure was already reported to the
        // chat when it happened, so the row's only remaining value is the log line above.
        var staleBackgroundJobs = (await db.BackgroundJobs
                .Where(j => j.Status != BackgroundJobStatus.Pending)
                .ToListAsync(cancellationToken))
            .Where(j => j.EnqueuedAt < updateCutoff)
            .ToList();
        db.BackgroundJobs.RemoveRange(staleBackgroundJobs);

        var staleConversations = (await db.Conversations.ToListAsync(cancellationToken))
            .Where(c => c.UpdatedAt < conversationCutoff)
            .ToList();
        db.Conversations.RemoveRange(staleConversations);

        // Replies can quote vault content, so the transcript is not kept forever. Ids are
        // AUTOINCREMENT, so pruning never lets a later message reuse a client's sync cursor.
        var historyCutoff = now - TimeSpan.FromDays(_webOptions.HistoryRetentionDays);
        var staleWebMessages = (await db.WebMessages.ToListAsync(cancellationToken))
            .Where(m => m.CreatedAt < historyCutoff)
            .ToList();
        db.WebMessages.RemoveRange(staleWebMessages);

        var prunedCount = staleUpdates.Count + staleJobs.Count + staleBackgroundJobs.Count + staleConversations.Count + staleWebMessages.Count;
        if (prunedCount > 0)
        {
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Pruned durability state: {Updates} processed update(s), {Jobs} completed job(s), {BackgroundJobs} finished background job(s), {Conversations} expired conversation(s), {WebMessages} web message(s).",
                staleUpdates.Count,
                staleJobs.Count,
                staleBackgroundJobs.Count,
                staleConversations.Count,
                staleWebMessages.Count);
        }
    }
}
