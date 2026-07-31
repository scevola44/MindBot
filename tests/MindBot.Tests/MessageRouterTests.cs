using MindBot.Core.Durability;
using MindBot.Core.Ingest;
using MindBot.Core.Notes;
using MindBot.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace MindBot.Tests;

public class MessageRouterTests
{
    private const long ChatId = 42;
    private const long SenderId = 7;

    private static (MessageRouter Router, InMemoryIngestUnitOfWork UnitOfWork) CreateRouter()
    {
        var planner = new NotePlanner(new FixedTimeProvider(new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.Zero)));
        var router = new MessageRouter(planner, NullLogger<MessageRouter>.Instance);
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
}
