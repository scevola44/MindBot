using MindBot.Core.Durability;
using Microsoft.EntityFrameworkCore;

namespace MindBot.Infrastructure.State;

public sealed class EfBackgroundJobQueue(IDbContextFactory<MindBotDbContext> dbContextFactory) : IBackgroundJobQueue
{
    public async Task<BackgroundJob?> GetNextPendingAsync(string kind, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Kind and status filter in SQL; the NextAttemptAt cutoff is applied client-side because
        // SQLite's EF provider cannot translate relational comparisons on DateTimeOffset at all
        // (the same constraint StateMaintenance.PruneAsync works around). Pending jobs are a
        // handful at most -- one per outstanding /ytsummary -- so materializing them is cheap.
        var pending = await db.BackgroundJobs
            .AsNoTracking()
            .Where(j => j.Kind == kind && j.Status == BackgroundJobStatus.Pending)
            .OrderBy(j => j.Id)
            .ToListAsync(cancellationToken);

        return pending.FirstOrDefault(j => j.NextAttemptAt <= now)?.ToDomain();
    }

    public async Task RecordFailureAsync(long jobId, string error, DateTimeOffset nextAttemptAt, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await db.BackgroundJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (entity is null)
        {
            return;
        }

        entity.Attempts++;
        entity.LastError = error;
        entity.NextAttemptAt = nextAttemptAt;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(long jobId, string error, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await db.BackgroundJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (entity is null)
        {
            return;
        }

        entity.Attempts++;
        entity.Status = BackgroundJobStatus.Failed;
        entity.LastError = error;
        await db.SaveChangesAsync(cancellationToken);
    }
}
