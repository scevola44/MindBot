using MindBot.Core.Commands;
using MindBot.Core.Durability;
using MindBot.Core.Health;
using MindBot.Core.Ingest;
using MindBot.Core.Notes;
using MindBot.Core.Operations;
using MindBot.Core.Options;
using MindBot.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MindBot.Tests;

public class MessageRouterTests
{
    private const long ChatId = 42;
    private const long SenderId = 7;

    private static (MessageRouter Router, InMemoryIngestUnitOfWork UnitOfWork) CreateRouter()
    {
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.Zero));
        var planner = new NotePlanner(timeProvider);

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddSingleton(Options.Create(new VaultOptions { Root = "/unused-in-these-tests" }));
        services.AddSingleton<VaultOperationApplier>();
        services.AddSingleton<IVaultOperationHandler, CreateNoteHandler>();
        services.AddSingleton<IVaultOperationHandler, AppendToNoteHandler>();
        services.AddSingleton(new HealthReportService(new HealthSnapshot(), new InMemoryWriteJobQueue(), timeProvider));
        services.AddSingleton<ICommand, AppendCommand>();
        services.AddSingleton<ICommand, StatusCommand>();
        services.AddSingleton<ICommand, PreviewCommand>();
        services.AddSingleton<ICommand, BareTextCommand>(); // catch-all: must stay last
        services.AddSingleton<CommandDispatcher>();
        services.AddSingleton<CommandExecutor>();

        var executor = services.BuildServiceProvider().GetRequiredService<CommandExecutor>();
        var router = new MessageRouter(planner, timeProvider, NullLogger<MessageRouter>.Instance, executor);
        return (router, new InMemoryIngestUnitOfWork());
    }

    private static Task<string> RouteAsync(MessageRouter router, InMemoryIngestUnitOfWork unitOfWork, string text, long updateId = 1) =>
        router.RouteAsync(unitOfWork, updateId, ChatId, SenderId, text);

    [Fact]
    public async Task RouteAsync_New_AsksForName_AndSetsState()
    {
        var (router, unitOfWork) = CreateRouter();

        var reply = await RouteAsync(router, unitOfWork, "/new");

        Assert.Equal("What would you like to name the note?", reply);
        Assert.Equal(ConversationStage.AwaitingNoteName, unitOfWork.Conversation(ChatId).Stage);
    }

    [Fact]
    public async Task RouteAsync_NewCaseInsensitive_Matches()
    {
        var (router, unitOfWork) = CreateRouter();

        await RouteAsync(router, unitOfWork, "/NEW");

        Assert.Equal(ConversationStage.AwaitingNoteName, unitOfWork.Conversation(ChatId).Stage);
    }

    [Fact]
    public async Task RouteAsync_NameProvided_AsksForContent_AndStoresName()
    {
        var (router, unitOfWork) = CreateRouter();
        await RouteAsync(router, unitOfWork, "/new");

        var reply = await RouteAsync(router, unitOfWork, "Groceries", updateId: 2);

        Assert.Equal("Got it. Now send me the note content.", reply);
        var state = unitOfWork.Conversation(ChatId);
        Assert.Equal(ConversationStage.AwaitingNoteContent, state.Stage);
        Assert.Equal("Groceries", state.PendingNoteName);
    }

    [Fact]
    public async Task RouteAsync_ContentProvided_QueuesNamedNote_ReturnsFilename_AndClearsState()
    {
        var (router, unitOfWork) = CreateRouter();
        await RouteAsync(router, unitOfWork, "/new");
        await RouteAsync(router, unitOfWork, "Groceries", updateId: 2);

        var reply = await RouteAsync(router, unitOfWork, "Milk and eggs", updateId: 3);

        Assert.Equal("groceries.md", reply);
        Assert.Equal(ConversationState.None, unitOfWork.Conversation(ChatId));

        var queued = Assert.Single(unitOfWork.Enqueued);
        Assert.Equal("groceries.md", queued.Filename);
        Assert.Contains("Milk and eggs", queued.Content);
        Assert.Equal(3, queued.UpdateId);
    }

    [Fact]
    public async Task RouteAsync_CancelWithPendingConversation_ClearsState_ReturnsCancelled()
    {
        var (router, unitOfWork) = CreateRouter();
        await RouteAsync(router, unitOfWork, "/new");

        var reply = await RouteAsync(router, unitOfWork, "/cancel", updateId: 2);

        Assert.Equal("Cancelled.", reply);
        Assert.Equal(ConversationState.None, unitOfWork.Conversation(ChatId));
    }

    [Fact]
    public async Task RouteAsync_CancelWithNoPendingConversation_ReturnsNothingToCancel()
    {
        var (router, unitOfWork) = CreateRouter();

        var reply = await RouteAsync(router, unitOfWork, "/cancel");

        Assert.Equal("Nothing to cancel.", reply);
    }

    [Fact]
    public async Task RouteAsync_PlainTextNoConversation_QueuesTimestampNote_ReturnsFilename()
    {
        var (router, unitOfWork) = CreateRouter();

        var reply = await RouteAsync(router, unitOfWork, "Just a quick thought");

        Assert.Equal("202607300900.md", reply);
        Assert.Single(unitOfWork.Enqueued);
    }

    [Fact]
    public async Task RouteAsync_TwoMessagesInTheSameMinute_GetDistinctFilenames()
    {
        var (router, unitOfWork) = CreateRouter();

        var first = await RouteAsync(router, unitOfWork, "one", updateId: 1);
        var second = await RouteAsync(router, unitOfWork, "two", updateId: 2);

        Assert.Equal("202607300900.md", first);
        Assert.Equal("202607300900-2.md", second);
        Assert.Equal(2, unitOfWork.Enqueued.Count);
    }

    [Fact]
    public async Task RouteAsync_NewRestartsPendingConversation()
    {
        var (router, unitOfWork) = CreateRouter();
        await RouteAsync(router, unitOfWork, "/new");
        await RouteAsync(router, unitOfWork, "First name", updateId: 2);

        await RouteAsync(router, unitOfWork, "/new", updateId: 3);

        Assert.Equal(ConversationStage.AwaitingNoteName, unitOfWork.Conversation(ChatId).Stage);
    }

    [Fact]
    public async Task RouteAsync_AwaitingContentWithNoPendingName_RecoversAndClearsState()
    {
        var (router, unitOfWork) = CreateRouter();
        await unitOfWork.SetConversationAsync(ChatId, new ConversationState(ConversationStage.AwaitingNoteContent));

        var reply = await RouteAsync(router, unitOfWork, "some content");

        Assert.Contains("start again with /new", reply);
        Assert.Equal(ConversationState.None, unitOfWork.Conversation(ChatId));
        Assert.Empty(unitOfWork.Enqueued);
    }

    [Fact]
    public async Task RouteAsync_CommandAddressedToTheBot_IsRecognised()
    {
        var (router, unitOfWork) = CreateRouter();

        await RouteAsync(router, unitOfWork, "/new@mybot");

        Assert.Equal(ConversationStage.AwaitingNoteName, unitOfWork.Conversation(ChatId).Stage);
    }

    [Fact]
    public async Task RouteAsync_Task_CreatesDailyNote_WhenNoneExistsYet()
    {
        var (router, unitOfWork) = CreateRouter();

        var reply = await RouteAsync(router, unitOfWork, "/task  Buy groceries");

        Assert.Equal("Added to TODO - 2026-07-30.md.", reply);

        var queued = Assert.Single(unitOfWork.Enqueued);
        Assert.Equal("06 - Daily Notes/2026/07 - July", queued.RelativeFolder);
        Assert.Equal("TODO - 2026-07-30.md", queued.Filename);
        Assert.Contains("date: 2026-07-30T09:00", queued.Content);
        Assert.Contains("last-modified: 2026-07-30T09:00", queued.Content);
        Assert.Contains("- ToDo", queued.Content);
        Assert.EndsWith("- [ ] Buy groceries\n", queued.Content);
    }

    [Fact]
    public async Task RouteAsync_Task_SecondCallSameDay_AppendsAndPreservesOriginalDate()
    {
        var (router, unitOfWork) = CreateRouter();
        await RouteAsync(router, unitOfWork, "/task Send mail", updateId: 1);

        var reply = await RouteAsync(router, unitOfWork, "/task Buy groceries", updateId: 2);

        Assert.Equal("Added to TODO - 2026-07-30.md.", reply);
        Assert.Equal(2, unitOfWork.Enqueued.Count);

        var second = unitOfWork.Enqueued[1];
        Assert.Contains("date: 2026-07-30T09:00", second.Content);
        Assert.Contains("- [ ] Send mail", second.Content);
        Assert.Contains("- [ ] Buy groceries", second.Content);
        Assert.True(second.Content.IndexOf("Send mail") < second.Content.IndexOf("Buy groceries"));
    }

    [Fact]
    public async Task RouteAsync_Task_MultiLineMessage_AddsOneItemPerLine()
    {
        var (router, unitOfWork) = CreateRouter();

        var reply = await RouteAsync(router, unitOfWork, "/task Task number 1\nTask number 2\nTask number 3");

        Assert.Equal("Added 3 items to TODO - 2026-07-30.md.", reply);
        var queued = Assert.Single(unitOfWork.Enqueued);
        Assert.Contains("- [ ] Task number 1", queued.Content);
        Assert.Contains("- [ ] Task number 2", queued.Content);
        Assert.Contains("- [ ] Task number 3", queued.Content);
    }

    [Fact]
    public async Task RouteAsync_TodoAlias_BehavesLikeTask()
    {
        var (router, unitOfWork) = CreateRouter();

        var reply = await RouteAsync(router, unitOfWork, "/todo Buy groceries");

        Assert.Equal("Added to TODO - 2026-07-30.md.", reply);
        Assert.Single(unitOfWork.Enqueued);
    }

    [Fact]
    public async Task RouteAsync_TaskWithNoItems_ReturnsUsage_AndQueuesNothing()
    {
        var (router, unitOfWork) = CreateRouter();

        var reply = await RouteAsync(router, unitOfWork, "/task");

        Assert.Contains("Usage:", reply);
        Assert.Empty(unitOfWork.Enqueued);
    }
}
