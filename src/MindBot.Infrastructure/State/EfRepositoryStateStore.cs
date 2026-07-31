using MindBot.Core.Durability;
using Microsoft.EntityFrameworkCore;

namespace MindBot.Infrastructure.State;

public sealed class EfRepositoryStateStore(IDbContextFactory<MindBotDbContext> dbContextFactory) : IRepositoryStateStore
{
    public async Task<RepositoryState> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await db.RepositoryState
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == RepositoryStateEntity.SingletonId, cancellationToken);

        return entity is null
            ? new RepositoryState(null, 0, null)
            : new RepositoryState(entity.LastPushedSha, entity.LastTelegramOffset, entity.LastSuccessfulPushAt);
    }

    public async Task SetLastPushedShaAsync(string sha, DateTimeOffset pushedAt, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await db.RepositoryState
            .FirstOrDefaultAsync(s => s.Id == RepositoryStateEntity.SingletonId, cancellationToken);

        if (entity is null)
        {
            entity = new RepositoryStateEntity();
            db.RepositoryState.Add(entity);
        }

        entity.LastPushedSha = sha;
        entity.LastSuccessfulPushAt = pushedAt;

        await db.SaveChangesAsync(cancellationToken);
    }
}
