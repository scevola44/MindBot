using MindBot.Core.Durability;
using MindBot.Core.Notes;
using MindBot.Core.Options;
using MindBot.Infrastructure.State;
using MindBot.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MindBot.Tests;

/// <summary>
/// The channel dimension against a real SQLite file: ids from different channels must never
/// collide in the duplicate guard or in /new state, and upgrading a database written before
/// channels existed must keep every row (SQLite rebuilds a table to change its primary key).
/// </summary>
public sealed class ChannelPersistenceTests : IAsyncLifetime
{
    private const string MigrationBeforeChannels = "20260805000000_AddBackgroundJobs";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "mindbot-channel-tests-" + Guid.NewGuid());
    private ServiceProvider _services = null!;
    private FixedTimeProvider _time = null!;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        var vaultRoot = Path.Combine(_root, "vault");
        Directory.CreateDirectory(Path.Combine(vaultRoot, VaultLayout.FleetingFolder));

        _time = new FixedTimeProvider(new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton(Options.Create(new VaultOptions { Root = vaultRoot }));
        services.AddSingleton(Options.Create(new StateOptions
        {
            DatabasePath = Path.Combine(_root, "mindbot.db"),
            ConversationExpiryMinutes = 60,
            ProcessedUpdateRetentionDays = 7,
        }));
        services.AddMindBotState();
        _services = services.BuildServiceProvider();

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private IIngestUnitOfWorkFactory Factory => _services.GetRequiredService<IIngestUnitOfWorkFactory>();

    [Fact]
    public async Task TheSameUpdateIdOnTwoChannels_IsTwoDistinctMessages()
    {
        await MigrateAsync();

        await using (var unitOfWork = await Factory.BeginAsync())
        {
            await unitOfWork.MarkUpdateProcessedAsync(Channel.Telegram, 100);
            await unitOfWork.CommitAsync();
        }

        await using (var unitOfWork = await Factory.BeginAsync())
        {
            Assert.True(await unitOfWork.IsUpdateProcessedAsync(Channel.Telegram, 100));
            Assert.False(await unitOfWork.IsUpdateProcessedAsync(Channel.Web, 100));

            // Would violate the primary key if the guard were still keyed on the id alone.
            await unitOfWork.MarkUpdateProcessedAsync(Channel.Web, 100);
            await unitOfWork.CommitAsync();
        }

        await using var check = await Factory.BeginAsync();
        Assert.True(await check.IsUpdateProcessedAsync(Channel.Web, 100));
    }

    [Fact]
    public async Task ConversationState_IsIsolatedPerChannel()
    {
        await MigrateAsync();

        await using (var unitOfWork = await Factory.BeginAsync())
        {
            await unitOfWork.SetConversationAsync(new ChatAddress(Channel.Web, 42), new ConversationState(ConversationStage.AwaitingNoteName));
            await unitOfWork.CommitAsync();
        }

        await using (var unitOfWork = await Factory.BeginAsync())
        {
            // Same chat id, other channel: a /new started on the web must not capture a Telegram message.
            Assert.Equal(ConversationState.None, await unitOfWork.GetConversationAsync(ChatAddress.Telegram(42)));
            Assert.Equal(ConversationStage.AwaitingNoteName, (await unitOfWork.GetConversationAsync(new ChatAddress(Channel.Web, 42))).Stage);

            await unitOfWork.ClearConversationAsync(ChatAddress.Telegram(42));
            await unitOfWork.CommitAsync();
        }

        await using var check = await Factory.BeginAsync();
        Assert.Equal(ConversationStage.AwaitingNoteName, (await check.GetConversationAsync(new ChatAddress(Channel.Web, 42))).Stage);
    }

    [Fact]
    public async Task Jobs_RoundTripTheirChannel()
    {
        await MigrateAsync();
        var web = new ChatAddress(Channel.Web, 1);

        await using (var unitOfWork = await Factory.BeginAsync())
        {
            await unitOfWork.EnqueueWriteJobAsync(5, VaultLayout.FleetingFolder, "202607311200.md", "hello", web, 7);
            await unitOfWork.EnqueueBackgroundJobAsync(5, BackgroundJobKinds.YouTubeSummary, "{}", web, 7);
            await unitOfWork.MarkUpdateProcessedAsync(Channel.Web, 5);
            await unitOfWork.CommitAsync();
        }

        var write = Assert.Single(await _services.GetRequiredService<IWriteJobQueue>().GetPendingAsync(10));
        Assert.Equal(web, write.Chat);

        var background = await _services.GetRequiredService<IBackgroundJobQueue>().GetNextPendingAsync(BackgroundJobKinds.YouTubeSummary, _time.Now);
        Assert.Equal(web, background!.Chat);
    }

    [Fact]
    public async Task UpgradingADatabaseFromBeforeChannels_KeepsEveryRowAsTelegram()
    {
        var dbFactory = _services.GetRequiredService<IDbContextFactory<MindBotDbContext>>();
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            await db.GetService<IMigrator>().MigrateAsync(MigrationBeforeChannels);

            // Written against the pre-channel schema, exactly as a running bot would have left it.
            const string at = "2026-07-31 11:59:00+00:00";
            await db.Database.ExecuteSqlRawAsync(
                $"""
                 INSERT INTO ProcessedUpdates (UpdateId, ReceivedAt) VALUES (100, '{at}');
                 INSERT INTO Conversations (ChatId, Stage, PendingNoteName, UpdatedAt) VALUES (42, 2, 'Groceries', '{at}');
                 INSERT INTO WriteJobs (UpdateId, RelativeFolder, Filename, Content, ChatId, SenderId, EnqueuedAt, Status)
                     VALUES (100, '{VaultLayout.FleetingFolder}', 'a.md', 'pending note', 42, 7, '{at}', 0);
                 INSERT INTO BackgroundJobs (UpdateId, Kind, Payload, ChatId, SenderId, Status, Attempts, LastError, EnqueuedAt, NextAttemptAt)
                     VALUES (101, '{BackgroundJobKinds.YouTubeSummary}', 'payload', 42, 7, 0, 0, NULL, '{at}', '{at}');
                 """);

            await db.Database.MigrateAsync();
        }

        await using (var unitOfWork = await Factory.BeginAsync())
        {
            Assert.True(await unitOfWork.IsUpdateProcessedAsync(Channel.Telegram, 100));
            Assert.False(await unitOfWork.IsUpdateProcessedAsync(Channel.Web, 100));

            var conversation = await unitOfWork.GetConversationAsync(ChatAddress.Telegram(42));
            Assert.Equal(ConversationStage.AwaitingNoteContent, conversation.Stage);
            Assert.Equal("Groceries", conversation.PendingNoteName);
        }

        var write = Assert.Single(await _services.GetRequiredService<IWriteJobQueue>().GetPendingAsync(10));
        Assert.Equal(ChatAddress.Telegram(42), write.Chat);
        Assert.Equal("pending note", write.Content);

        var background = await _services.GetRequiredService<IBackgroundJobQueue>().GetNextPendingAsync(BackgroundJobKinds.YouTubeSummary, _time.Now);
        Assert.Equal(ChatAddress.Telegram(42), background!.Chat);
        Assert.Equal(101, background.UpdateId);
    }

    private async Task MigrateAsync()
    {
        await using var db = await _services.GetRequiredService<IDbContextFactory<MindBotDbContext>>().CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }
}
