using MindBot.Core.Durability;
using MindBot.Core.Notes;
using MindBot.Core.Options;
using MindBot.Infrastructure.State;
using MindBot.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MindBot.Tests;

/// <summary>
/// Exercises the durability layer against a real on-disk SQLite database, including the migration
/// that creates it. These are the guarantees the whole "no lost, no duplicated notes" claim rests
/// on, so they are tested against the real store rather than a fake.
/// </summary>
public sealed class IngestPersistenceTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mindbot-state-tests-" + Guid.NewGuid());
    private string _databasePath = string.Empty;
    private string _vaultRoot = string.Empty;
    private ServiceProvider _services = null!;
    private FixedTimeProvider _time = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _databasePath = Path.Combine(_root, "mindbot.db");
        _vaultRoot = Path.Combine(_root, "vault");
        Directory.CreateDirectory(Path.Combine(_vaultRoot, VaultLayout.FleetingFolder));

        _time = new FixedTimeProvider(new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton(Options.Create(new VaultOptions { Root = _vaultRoot }));
        services.AddSingleton(Options.Create(new StateOptions
        {
            DatabasePath = _databasePath,
            ConversationExpiryMinutes = 60,
            ProcessedUpdateRetentionDays = 7,
        }));
        services.AddMindBotState();

        _services = services.BuildServiceProvider();

        var factory = _services.GetRequiredService<IDbContextFactory<MindBotDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
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

    private IWriteJobQueue Queue => _services.GetRequiredService<IWriteJobQueue>();

    [Fact]
    public async Task Migration_CreatesAWorkingSchema()
    {
        // If the hand-authored migration and the model disagree, MigrateAsync in InitializeAsync
        // would already have thrown. This asserts the tables are actually usable.
        Assert.Equal(0, await Queue.GetPendingCountAsync());

        await using var unitOfWork = await Factory.BeginAsync();
        await unitOfWork.EnqueueWriteJobAsync(1, VaultLayout.FleetingFolder, "202607311200.md", "hello", 42, 7);
        await unitOfWork.MarkUpdateProcessedAsync(1);
        await unitOfWork.CommitAsync();

        Assert.Equal(1, await Queue.GetPendingCountAsync());
    }

    [Fact]
    public async Task SameUpdateDeliveredTwice_ProducesExactlyOneJob()
    {
        var first = await AcceptUpdateAsync(updateId: 100, "202607311200.md", "first delivery");

        // Telegram redelivers because the offset was never confirmed before the crash.
        var second = await AcceptUpdateAsync(updateId: 100, "202607311200.md", "first delivery");

        Assert.Equal("202607311200.md", first);
        Assert.Equal(string.Empty, second);
        Assert.Equal(1, await Queue.GetPendingCountAsync());
    }

    [Fact]
    public async Task UncommittedUnitOfWork_LeavesNoTrace()
    {
        await using (var unitOfWork = await Factory.BeginAsync())
        {
            await unitOfWork.EnqueueWriteJobAsync(200, VaultLayout.FleetingFolder, "202607311200.md", "never committed", 42, 7);
            await unitOfWork.MarkUpdateProcessedAsync(200);
            // Disposed without CommitAsync — the crash-before-commit case.
        }

        Assert.Equal(0, await Queue.GetPendingCountAsync());

        await using var check = await Factory.BeginAsync();
        Assert.False(await check.IsUpdateProcessedAsync(200));
    }

    [Fact]
    public async Task ReserveFilenameAsync_ExistingNoteOnDisk_AllocatesASuffix()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_vaultRoot, VaultLayout.RelativeNotePath("202607311200.md")),
            "already here");

        await using var unitOfWork = await Factory.BeginAsync();
        var filename = await unitOfWork.ReserveFilenameAsync("202607311200.md");

        Assert.Equal("202607311200-2.md", filename);
    }

    [Fact]
    public async Task ReserveFilenameAsync_PendingJobHoldsTheName_AllocatesASuffix()
    {
        await AcceptUpdateAsync(updateId: 300, "202607311200.md", "first");

        await using var unitOfWork = await Factory.BeginAsync();
        var filename = await unitOfWork.ReserveFilenameAsync("202607311200.md");

        Assert.Equal("202607311200-2.md", filename);
    }

    [Fact]
    public async Task GetLatestNoteContentAsync_NoteMissing_ReturnsNull()
    {
        await using var unitOfWork = await Factory.BeginAsync();

        var content = await unitOfWork.GetLatestNoteContentAsync(VaultLayout.DailyNotesFolder, "TODO - 2026-07-31.md");

        Assert.Null(content);
    }

    [Fact]
    public async Task GetLatestNoteContentAsync_ReadsFromDisk_WhenNoPendingJobExists()
    {
        var folder = Path.Combine(_vaultRoot, VaultLayout.DailyNotesFolder);
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "TODO - 2026-07-31.md"), "on disk content");

        await using var unitOfWork = await Factory.BeginAsync();
        var content = await unitOfWork.GetLatestNoteContentAsync(VaultLayout.DailyNotesFolder, "TODO - 2026-07-31.md");

        Assert.Equal("on disk content", content);
    }

    [Fact]
    public async Task GetLatestNoteContentAsync_PrefersPendingJobOverDisk()
    {
        var folder = Path.Combine(_vaultRoot, VaultLayout.DailyNotesFolder);
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "TODO - 2026-07-31.md"), "stale disk content");

        await using (var unitOfWork = await Factory.BeginAsync())
        {
            await unitOfWork.EnqueueWriteJobAsync(700, VaultLayout.DailyNotesFolder, "TODO - 2026-07-31.md", "fresher pending content", 42, 7);
            await unitOfWork.MarkUpdateProcessedAsync(700);
            await unitOfWork.CommitAsync();
        }

        await using var next = await Factory.BeginAsync();
        var content = await next.GetLatestNoteContentAsync(VaultLayout.DailyNotesFolder, "TODO - 2026-07-31.md");

        Assert.Equal("fresher pending content", content);
    }

    [Fact]
    public async Task TenMessagesInTheSameMinute_ProduceTenDistinctFilenames()
    {
        var filenames = new List<string>();
        for (var i = 0; i < 10; i++)
        {
            filenames.Add(await AcceptUpdateAsync(updateId: 400 + i, "202607311200.md", $"note {i}"));
        }

        Assert.Equal(10, filenames.Distinct().Count());
        Assert.Equal("202607311200.md", filenames[0]);
        Assert.Equal("202607311200-2.md", filenames[1]);
        Assert.Equal("202607311200-10.md", filenames[9]);
        Assert.Equal(10, await Queue.GetPendingCountAsync());
    }

    [Fact]
    public async Task ConversationState_SurvivesAcrossUnitsOfWork()
    {
        await using (var unitOfWork = await Factory.BeginAsync())
        {
            await unitOfWork.SetConversationAsync(42, new ConversationState(ConversationStage.AwaitingNoteContent, "Groceries"));
            await unitOfWork.CommitAsync();
        }

        await using var next = await Factory.BeginAsync();
        var state = await next.GetConversationAsync(42);

        Assert.Equal(ConversationStage.AwaitingNoteContent, state.Stage);
        Assert.Equal("Groceries", state.PendingNoteName);
    }

    [Fact]
    public async Task ConversationState_PastItsExpiry_IsTreatedAsAbsent()
    {
        await using (var unitOfWork = await Factory.BeginAsync())
        {
            await unitOfWork.SetConversationAsync(42, new ConversationState(ConversationStage.AwaitingNoteName));
            await unitOfWork.CommitAsync();
        }

        // A conversation abandoned two hours ago must not swallow an unrelated later message.
        _time.Now = _time.Now.AddHours(2);

        await using var next = await Factory.BeginAsync();
        var state = await next.GetConversationAsync(42);

        Assert.Equal(ConversationState.None, state);
    }

    [Fact]
    public async Task MarkCompletedAsync_RemovesJobsFromThePendingSet()
    {
        await AcceptUpdateAsync(updateId: 500, "202607311200.md", "hello");
        var pending = await Queue.GetPendingAsync(10);

        await Queue.MarkCompletedAsync(pending.Select(j => j.Id).ToArray());

        Assert.Equal(0, await Queue.GetPendingCountAsync());
    }

    [Fact]
    public async Task RepositoryState_RoundTripsLastPushedSha()
    {
        var store = _services.GetRequiredService<IRepositoryStateStore>();
        var pushedAt = new DateTimeOffset(2026, 7, 31, 12, 30, 0, TimeSpan.Zero);

        await store.SetLastPushedShaAsync("abc123", pushedAt);
        var state = await store.GetAsync();

        Assert.Equal("abc123", state.LastPushedSha);
        Assert.Equal(pushedAt, state.LastSuccessfulPushAt);
    }

    [Fact]
    public async Task TelegramOffset_IsPersistedForResumeAfterRestart()
    {
        await using (var unitOfWork = await Factory.BeginAsync())
        {
            await unitOfWork.SetTelegramOffsetAsync(9876);
            await unitOfWork.CommitAsync();
        }

        var state = await _services.GetRequiredService<IRepositoryStateStore>().GetAsync();

        Assert.Equal(9876, state.LastTelegramOffset);
    }

    [Fact]
    public async Task PruneAsync_RemovesProcessedUpdatesPastRetention()
    {
        await AcceptUpdateAsync(updateId: 600, "202607311200.md", "hello");

        _time.Now = _time.Now.AddDays(30);
        await _services.GetRequiredService<StateMaintenance>().PruneAsync();

        await using var check = await Factory.BeginAsync();
        Assert.False(await check.IsUpdateProcessedAsync(600));
    }

    /// <summary>Mirrors what the polling loop does for one accepted update.</summary>
    private async Task<string> AcceptUpdateAsync(long updateId, string baseFilename, string content)
    {
        await using var unitOfWork = await Factory.BeginAsync();

        if (await unitOfWork.IsUpdateProcessedAsync(updateId))
        {
            return string.Empty;
        }

        var filename = await unitOfWork.ReserveFilenameAsync(baseFilename);
        await unitOfWork.EnqueueWriteJobAsync(updateId, VaultLayout.FleetingFolder, filename, content, 42, 7);
        await unitOfWork.MarkUpdateProcessedAsync(updateId);
        await unitOfWork.SetTelegramOffsetAsync((int)updateId + 1);
        await unitOfWork.CommitAsync();

        return filename;
    }
}
