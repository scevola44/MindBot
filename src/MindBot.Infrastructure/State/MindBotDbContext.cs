using Microsoft.EntityFrameworkCore;

namespace MindBot.Infrastructure.State;

public sealed class MindBotDbContext(DbContextOptions<MindBotDbContext> options) : DbContext(options)
{
    public DbSet<ProcessedUpdateEntity> ProcessedUpdates => Set<ProcessedUpdateEntity>();

    public DbSet<WriteJobEntity> WriteJobs => Set<WriteJobEntity>();

    public DbSet<BackgroundJobEntity> BackgroundJobs => Set<BackgroundJobEntity>();

    public DbSet<ConversationStateEntity> Conversations => Set<ConversationStateEntity>();

    public DbSet<RepositoryStateEntity> RepositoryState => Set<RepositoryStateEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProcessedUpdateEntity>(entity =>
        {
            entity.ToTable("ProcessedUpdates");
            entity.HasKey(e => e.UpdateId);
            // Telegram assigns update IDs, so they are never generated on our side.
            entity.Property(e => e.UpdateId).ValueGeneratedNever();
            entity.Property(e => e.ReceivedAt).IsRequired();
            entity.HasIndex(e => e.ReceivedAt);
        });

        modelBuilder.Entity<WriteJobEntity>(entity =>
        {
            entity.ToTable("WriteJobs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.UpdateId).IsRequired();
            entity.Property(e => e.RelativeFolder).IsRequired();
            entity.Property(e => e.Filename).IsRequired();
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.ChatId).IsRequired();
            entity.Property(e => e.SenderId).IsRequired();
            entity.Property(e => e.EnqueuedAt).IsRequired();
            // Enums map to INTEGER by default; left unconfigured so the model stays vanilla and
            // the checked-in migration snapshot is easy to regenerate.
            entity.Property(e => e.Status).IsRequired();

            // Drives both the pending scan and the filename-reservation/latest-content probes.
            entity.HasIndex(e => new { e.Status, e.Id });
            entity.HasIndex(e => new { e.RelativeFolder, e.Filename, e.Status });
        });

        modelBuilder.Entity<BackgroundJobEntity>(entity =>
        {
            entity.ToTable("BackgroundJobs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.UpdateId).IsRequired();
            entity.Property(e => e.Kind).IsRequired();
            entity.Property(e => e.Payload).IsRequired();
            entity.Property(e => e.ChatId).IsRequired();
            entity.Property(e => e.SenderId).IsRequired();
            entity.Property(e => e.Status).IsRequired();
            entity.Property(e => e.Attempts).IsRequired();
            entity.Property(e => e.LastError);
            entity.Property(e => e.EnqueuedAt).IsRequired();
            entity.Property(e => e.NextAttemptAt).IsRequired();

            // Drives the worker's claim scan: pending jobs of one kind, oldest first.
            entity.HasIndex(e => new { e.Kind, e.Status, e.Id });
        });

        modelBuilder.Entity<ConversationStateEntity>(entity =>
        {
            entity.ToTable("Conversations");
            entity.HasKey(e => e.ChatId);
            entity.Property(e => e.ChatId).ValueGeneratedNever();
            entity.Property(e => e.Stage).IsRequired();
            entity.Property(e => e.PendingNoteName);
            entity.Property(e => e.UpdatedAt).IsRequired();
        });

        modelBuilder.Entity<RepositoryStateEntity>(entity =>
        {
            entity.ToTable("RepositoryState");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.LastPushedSha);
            entity.Property(e => e.LastTelegramOffset).IsRequired();
            entity.Property(e => e.LastSuccessfulPushAt);
        });
    }
}
