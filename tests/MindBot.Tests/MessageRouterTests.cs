using MindBot.Bot.Services;
using MindBot.Core.Notes;
using MindBot.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace MindBot.Tests;

public class MessageRouterTests
{
    private const long ChatId = 42;

    private static (MessageRouter Router, ConversationStateStore Store, FakeVaultWriter Vault) CreateRouter()
    {
        var store = new ConversationStateStore();
        var git = new FakeGitService();
        var vault = new FakeVaultWriter();
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.Zero));
        var pipeline = new NotePipeline(git, vault, time, NullLogger<NotePipeline>.Instance);
        var router = new MessageRouter(store, pipeline, NullLogger<MessageRouter>.Instance);
        return (router, store, vault);
    }

    [Fact]
    public async Task RouteAsync_New_AsksForName_AndSetsState()
    {
        var (router, store, _) = CreateRouter();

        var reply = await router.RouteAsync(ChatId, "/new");

        Assert.Equal("What would you like to name the note?", reply);
        Assert.Equal(ConversationStage.AwaitingNoteName, store.Get(ChatId).Stage);
    }

    [Fact]
    public async Task RouteAsync_NewCaseInsensitive_Matches()
    {
        var (router, store, _) = CreateRouter();

        await router.RouteAsync(ChatId, "/NEW");

        Assert.Equal(ConversationStage.AwaitingNoteName, store.Get(ChatId).Stage);
    }

    [Fact]
    public async Task RouteAsync_NameProvided_AsksForContent_AndStoresName()
    {
        var (router, store, _) = CreateRouter();
        await router.RouteAsync(ChatId, "/new");

        var reply = await router.RouteAsync(ChatId, "Groceries");

        Assert.Equal("Got it. Now send me the note content.", reply);
        var state = store.Get(ChatId);
        Assert.Equal(ConversationStage.AwaitingNoteContent, state.Stage);
        Assert.Equal("Groceries", state.PendingNoteName);
    }

    [Fact]
    public async Task RouteAsync_ContentProvided_CreatesNamedNote_ReturnsFilename_AndClearsState()
    {
        var (router, store, vault) = CreateRouter();
        await router.RouteAsync(ChatId, "/new");
        await router.RouteAsync(ChatId, "Groceries");

        var reply = await router.RouteAsync(ChatId, "Milk and eggs");

        Assert.Equal("groceries.md", reply);
        Assert.Equal(ConversationState.None, store.Get(ChatId));
        Assert.Single(vault.Written);
        Assert.Equal("groceries.md", vault.Written[0].Filename);
    }

    [Fact]
    public async Task RouteAsync_CancelWithPendingConversation_ClearsState_ReturnsCancelled()
    {
        var (router, store, _) = CreateRouter();
        await router.RouteAsync(ChatId, "/new");

        var reply = await router.RouteAsync(ChatId, "/cancel");

        Assert.Equal("Cancelled.", reply);
        Assert.Equal(ConversationState.None, store.Get(ChatId));
    }

    [Fact]
    public async Task RouteAsync_CancelWithNoPendingConversation_ReturnsNothingToCancel()
    {
        var (router, _, _) = CreateRouter();

        var reply = await router.RouteAsync(ChatId, "/cancel");

        Assert.Equal("Nothing to cancel.", reply);
    }

    [Fact]
    public async Task RouteAsync_PlainTextNoConversation_CreatesTimestampNote_ReturnsFilename()
    {
        var (router, _, vault) = CreateRouter();

        var reply = await router.RouteAsync(ChatId, "Just a quick thought");

        Assert.Equal("202607300900.md", reply);
        Assert.Single(vault.Written);
    }

    [Fact]
    public async Task RouteAsync_NewRestartsPendingConversation()
    {
        var (router, store, _) = CreateRouter();
        await router.RouteAsync(ChatId, "/new");
        await router.RouteAsync(ChatId, "First name");

        await router.RouteAsync(ChatId, "/new");

        Assert.Equal(ConversationStage.AwaitingNoteName, store.Get(ChatId).Stage);
    }

    [Fact]
    public async Task RouteAsync_NoteCreationThrows_StatePreservedForRetry()
    {
        var store = new ConversationStateStore();
        var git = new FakeGitService();
        var vault = new FakeVaultWriter { ThrowOnWrite = new InvalidOperationException("disk full") };
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.Zero));
        var pipeline = new NotePipeline(git, vault, time, NullLogger<NotePipeline>.Instance);
        var router = new MessageRouter(store, pipeline, NullLogger<MessageRouter>.Instance);
        await router.RouteAsync(ChatId, "/new");
        await router.RouteAsync(ChatId, "Groceries");

        await Assert.ThrowsAsync<InvalidOperationException>(() => router.RouteAsync(ChatId, "Milk and eggs"));

        var state = store.Get(ChatId);
        Assert.Equal(ConversationStage.AwaitingNoteContent, state.Stage);
        Assert.Equal("Groceries", state.PendingNoteName);
    }
}
