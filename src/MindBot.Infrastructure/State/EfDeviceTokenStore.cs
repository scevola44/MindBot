using MindBot.Core.Web;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MindBot.Infrastructure.State;

public sealed class EfDeviceTokenStore(IDbContextFactory<MindBotDbContext> dbContextFactory, TimeProvider timeProvider) : IDeviceTokenStore
{
    // Recording every request would turn each read into a write; a few minutes of precision is enough.
    private static readonly TimeSpan LastSeenResolution = TimeSpan.FromMinutes(5);

    private const int SqliteConstraintError = 19;

    public async Task<(DeviceInfo Device, string Token)> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("A device name is required.", nameof(name));
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var token = DeviceTokens.Generate();
        var entity = new DeviceTokenEntity
        {
            Name = trimmed,
            TokenHash = DeviceTokens.Hash(token),
            CreatedAt = timeProvider.GetUtcNow(),
        };

        db.DeviceTokens.Add(entity);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: SqliteConstraintError })
        {
            throw new InvalidOperationException($"An active device named '{trimmed}' already exists. Revoke it first, or pick another name.", ex);
        }

        return (entity.ToDomain(), token);
    }

    public async Task<IReadOnlyList<DeviceInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await db.DeviceTokens.AsNoTracking().OrderBy(d => d.Id).ToListAsync(cancellationToken);
        return entities.Select(e => e.ToDomain()).ToList();
    }

    public async Task<bool> RevokeAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var trimmed = name.Trim();
        var entity = await db.DeviceTokens.FirstOrDefaultAsync(d => d.Name == trimmed && d.RevokedAt == null, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        entity.RevokedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<DeviceInfo?> AuthenticateAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (!DeviceTokens.LooksValid(token))
        {
            return null;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var hash = DeviceTokens.Hash(token!);
        var entity = await db.DeviceTokens.FirstOrDefaultAsync(d => d.TokenHash == hash, cancellationToken);
        if (entity is null || entity.RevokedAt is not null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        if (entity.LastSeenAt is null || now - entity.LastSeenAt.Value > LastSeenResolution)
        {
            entity.LastSeenAt = now;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Bookkeeping only. A busy database must not turn a valid token into a 401.
            }
        }

        return entity.ToDomain();
    }
}
