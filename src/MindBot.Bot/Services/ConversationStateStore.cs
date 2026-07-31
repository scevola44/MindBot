namespace MindBot.Bot.Services;

public enum ConversationStage
{
    None,
    AwaitingNoteName,
    AwaitingNoteContent,
}

public sealed record ConversationState(ConversationStage Stage, string? PendingNoteName = null)
{
    public static readonly ConversationState None = new(ConversationStage.None);
}

/// <summary>Tracks the in-progress /new conversation, if any, per chat. In-memory only —
/// losing this on restart is acceptable for this bot's small set of authorized users.</summary>
public sealed class ConversationStateStore
{
    private readonly object _lock = new();
    private readonly Dictionary<long, ConversationState> _states = [];

    public ConversationState Get(long chatId)
    {
        lock (_lock)
        {
            return _states.TryGetValue(chatId, out var state) ? state : ConversationState.None;
        }
    }

    public void Set(long chatId, ConversationState state)
    {
        lock (_lock)
        {
            _states[chatId] = state;
        }
    }

    public void Clear(long chatId)
    {
        lock (_lock)
        {
            _states.Remove(chatId);
        }
    }
}
