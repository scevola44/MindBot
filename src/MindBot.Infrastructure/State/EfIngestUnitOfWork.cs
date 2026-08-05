using MindBot.Core.Durability;
using MindBot.Core.Notes;
using MindBot.Core.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace MindBot.Infrastructure.State;

public sealed class EfIngestUnitOfWorkFactory(
    IDbContextFactory<MindBotDbContext> dbContextFactory,
    IOptions<VaultOptions> vaultOptions,
    IOptions<StateOptions> stateOptions,
    TimeProvider timeProvider) : IIngestUnitOfWorkFactory
{
    public async Task<IIngestUnitOfWork> BeginAsync(CancellationToken cancellationToken = default)
    {
        var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        try
        {
            var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            return new EfIngestUnitOfWork(db, transaction, vaultOptions.Value, stateOptions.Value, timeProvider);
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }
}

/// <summary>
/// A SQLite transaction spanning everything one Telegram update changes. Individual operations
/// call SaveChanges so later steps in the same update (notably filename reservation) can see
/// earlier ones, but nothing becomes visible to another connection until <see cref="CommitAsync"/>.
/// </summary>
public sealed class EfIngestUnitOfWork(
    MindBotDbContext db,
    IDbContextTransaction transaction,
    VaultOptions vaultOptions,
    StateOptions stateOptions,
    TimeProvider timeProvider) : IIngestUnitOfWork
{
    private bool _committed;

    public Task<bool> IsUpdateProcessedAsync(long updateId, CancellationToken cancellationToken = default) =>
        db.ProcessedUpdates.AnyAsync(u => u.UpdateId == updateId, cancellationToken);

    public async Task<ConversationState> GetConversationAsync(long chatId, CancellationToken cancellationToken = default)
    {
        var entity = await db.Conversations.FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken);
        if (entity is null)
        {
            return ConversationState.None;
        }

        var age = timeProvider.GetUtcNow() - entity.UpdatedAt;
        if (age > TimeSpan.FromMinutes(stateOptions.ConversationExpiryMinutes))
        {
            // A conversation abandoned hours ago must not swallow an unrelated message as its body.
            db.Conversations.Remove(entity);
            await db.SaveChangesAsync(cancellationToken);
            return ConversationState.None;
        }

        return new ConversationState(entity.Stage, entity.PendingNoteName);
    }

    public async Task SetConversationAsync(long chatId, ConversationState state, CancellationToken cancellationToken = default)
    {
        var entity = await db.Conversations.FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken);
        if (entity is null)
        {
            entity = new ConversationStateEntity { ChatId = chatId };
            db.Conversations.Add(entity);
        }

        entity.Stage = state.Stage;
        entity.PendingNoteName = state.PendingNoteName;
        entity.UpdatedAt = timeProvider.GetUtcNow();

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ClearConversationAsync(long chatId, CancellationToken cancellationToken = default)
    {
        var entity = await db.Conversations.FirstOrDefaultAsync(c => c.ChatId == chatId, cancellationToken);
        if (entity is null)
        {
            return;
        }

        db.Conversations.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<string> ReserveFilenameAsync(string baseFilename, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            var candidate = NoteFilenameFactory.CreateCandidate(baseFilename, attempt);

            if (File.Exists(Path.Combine(vaultOptions.Root, VaultLayout.RelativeNotePath(candidate))))
            {
                continue;
            }

            // A pending job has claimed the name but has not been written to disk yet.
            var claimed = await db.WriteJobs
                .AnyAsync(
                    j => j.Status == WriteJobStatus.Pending
                        && j.RelativeFolder == VaultLayout.FleetingFolder
                        && j.Filename == candidate,
                    cancellationToken);

            if (!claimed)
            {
                return candidate;
            }
        }
    }

    public async Task<string?> GetLatestNoteContentAsync(string relativeFolder, string filename, CancellationToken cancellationToken = default)
    {
        var pending = await db.WriteJobs
            .Where(j => j.Status == WriteJobStatus.Pending && j.RelativeFolder == relativeFolder && j.Filename == filename)
            .OrderByDescending(j => j.Id)
            .Select(j => j.Content)
            .FirstOrDefaultAsync(cancellationToken);

        if (pending is not null)
        {
            return pending;
        }

        var path = Path.Combine(vaultOptions.Root, relativeFolder, filename);
        return File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;
    }

    public async Task EnqueueWriteJobAsync(
        long updateId,
        string relativeFolder,
        string filename,
        string content,
        long chatId,
        long senderId,
        CancellationToken cancellationToken = default)
    {
        db.WriteJobs.Add(new WriteJobEntity
        {
            UpdateId = updateId,
            RelativeFolder = relativeFolder,
            Filename = filename,
            Content = content,
            ChatId = chatId,
            SenderId = senderId,
            EnqueuedAt = timeProvider.GetUtcNow(),
            Status = WriteJobStatus.Pending,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task EnqueueBackgroundJobAsync(
        long updateId,
        string kind,
        string payload,
        long chatId,
        long senderId,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();

        db.BackgroundJobs.Add(new BackgroundJobEntity
        {
            UpdateId = updateId,
            Kind = kind,
            Payload = payload,
            ChatId = chatId,
            SenderId = senderId,
            Status = BackgroundJobStatus.Pending,
            Attempts = 0,
            EnqueuedAt = now,
            // Claimable immediately; only a failed attempt pushes this forward.
            NextAttemptAt = now,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteBackgroundJobAsync(long jobId, CancellationToken cancellationToken = default)
    {
        var entity = await db.BackgroundJobs.FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);
        if (entity is null)
        {
            return;
        }

        entity.Status = BackgroundJobStatus.Completed;
        entity.LastError = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkUpdateProcessedAsync(long updateId, CancellationToken cancellationToken = default)
    {
        db.ProcessedUpdates.Add(new ProcessedUpdateEntity
        {
            UpdateId = updateId,
            ReceivedAt = timeProvider.GetUtcNow(),
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetTelegramOffsetAsync(int offset, CancellationToken cancellationToken = default)
    {
        var state = await db.RepositoryState
            .FirstOrDefaultAsync(s => s.Id == RepositoryStateEntity.SingletonId, cancellationToken);

        if (state is null)
        {
            state = new RepositoryStateEntity();
            db.RepositoryState.Add(state);
        }

        state.LastTelegramOffset = offset;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await transaction.CommitAsync(cancellationToken);
        _committed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_committed)
        {
            // Nothing was durable, so Telegram will redeliver the update and it is processed afresh.
            try
            {
                await transaction.RollbackAsync();
            }
            catch (Exception)
            {
                // The transaction may already be gone (e.g. the connection dropped); disposal must not throw.
            }
        }

        await transaction.DisposeAsync();
        await db.DisposeAsync();
    }
}
