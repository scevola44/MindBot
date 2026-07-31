namespace MindBot.Core.Durability;

public enum ConversationStage
{
    None,
    AwaitingNoteName,
    AwaitingNoteContent,
}

/// <summary>
/// The in-progress /new conversation for one chat. Persisted so a restart between the note name
/// and the note content does not silently file the name as a fleeting note body.
/// </summary>
public sealed record ConversationState(ConversationStage Stage, string? PendingNoteName = null)
{
    public static readonly ConversationState None = new(ConversationStage.None);
}
