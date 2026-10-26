using MindBot.Core.Web;
using Microsoft.EntityFrameworkCore;

namespace MindBot.Infrastructure.State;

public sealed class EfWebTranscript(IDbContextFactory<MindBotDbContext> dbContextFactory, TimeProvider timeProvider) : IWebTranscript
{
    public async Task<WebMessage> AppendAsync(WebMessageDirection direction, string text, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var entity = new WebMessageEntity
        {
            Direction = direction,
            Text = text,
            CreatedAt = timeProvider.GetUtcNow(),
        };

        db.WebMessages.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity.ToDomain();
    }

    public async Task<IReadOnlyList<WebMessage>> GetAfterAsync(long afterId, int limit, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var entities = await db.WebMessages
            .AsNoTracking()
            .Where(m => m.Id > afterId)
            .OrderBy(m => m.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToDomain()).ToList();
    }
}
