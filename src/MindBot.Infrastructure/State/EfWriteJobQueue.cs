using MindBot.Core.Durability;
using Microsoft.EntityFrameworkCore;

namespace MindBot.Infrastructure.State;

public sealed class EfWriteJobQueue(IDbContextFactory<MindBotDbContext> dbContextFactory) : IWriteJobQueue
{
    public async Task<IReadOnlyList<WriteJob>> GetPendingAsync(int maxCount, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var entities = await db.WriteJobs
            .AsNoTracking()
            .Where(j => j.Status == WriteJobStatus.Pending)
            .OrderBy(j => j.Id)
            .Take(maxCount)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToDomain()).ToList();
    }

    public async Task<int> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.WriteJobs.CountAsync(j => j.Status == WriteJobStatus.Pending, cancellationToken);
    }

    public async Task MarkCompletedAsync(IReadOnlyCollection<long> jobIds, CancellationToken cancellationToken = default)
    {
        if (jobIds.Count == 0)
        {
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var ids = jobIds.ToArray();
        await db.WriteJobs
            .Where(j => ids.Contains(j.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, WriteJobStatus.Completed), cancellationToken);
    }
}
