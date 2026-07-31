using MindBot.Bot.Services;

namespace MindBot.Tests;

public class ConversationStateStoreTests
{
    [Fact]
    public void Get_UnknownChat_ReturnsNoneState()
    {
        var store = new ConversationStateStore();

        var state = store.Get(123);

        Assert.Equal(ConversationState.None, state);
    }

    [Fact]
    public void Set_ThenGet_ReturnsStoredState()
    {
        var store = new ConversationStateStore();
        var expected = new ConversationState(ConversationStage.AwaitingNoteContent, "Groceries");

        store.Set(123, expected);

        Assert.Equal(expected, store.Get(123));
    }

    [Fact]
    public void Clear_RemovesState_SubsequentGetReturnsNone()
    {
        var store = new ConversationStateStore();
        store.Set(123, new ConversationState(ConversationStage.AwaitingNoteName));

        store.Clear(123);

        Assert.Equal(ConversationState.None, store.Get(123));
    }

    [Fact]
    public void Set_DifferentChats_AreIndependent()
    {
        var store = new ConversationStateStore();

        store.Set(1, new ConversationState(ConversationStage.AwaitingNoteName));
        store.Set(2, new ConversationState(ConversationStage.AwaitingNoteContent, "Note"));

        Assert.Equal(ConversationStage.AwaitingNoteName, store.Get(1).Stage);
        Assert.Equal(ConversationStage.AwaitingNoteContent, store.Get(2).Stage);
    }
}
