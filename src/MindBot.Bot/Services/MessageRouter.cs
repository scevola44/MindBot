using MindBot.Core.Notes;
using Microsoft.Extensions.Logging;

namespace MindBot.Bot.Services;

/// <summary>
/// Decides what an incoming, already-authorized text message means: a /new or /cancel
/// command, a reply within a pending /new conversation, or plain text to file as a
/// timestamp-only fleeting note. Returns the reply text to send back to the chat.
/// </summary>
public sealed class MessageRouter(
    ConversationStateStore stateStore,
    NotePipeline notePipeline,
    ILogger<MessageRouter> logger)
{
    public async Task<string> RouteAsync(long chatId, string messageText, CancellationToken cancellationToken = default)
    {
        var command = ExtractCommand(messageText);

        if (string.Equals(command, "/cancel", StringComparison.OrdinalIgnoreCase))
        {
            if (stateStore.Get(chatId).Stage == ConversationStage.None)
            {
                return "Nothing to cancel.";
            }

            stateStore.Clear(chatId);
            return "Cancelled.";
        }

        if (string.Equals(command, "/new", StringComparison.OrdinalIgnoreCase))
        {
            stateStore.Set(chatId, new ConversationState(ConversationStage.AwaitingNoteName));
            return "What would you like to name the note?";
        }

        var state = stateStore.Get(chatId);
        switch (state.Stage)
        {
            case ConversationStage.AwaitingNoteName:
                stateStore.Set(chatId, state with { Stage = ConversationStage.AwaitingNoteContent, PendingNoteName = messageText.Trim() });
                return "Got it. Now send me the note content.";

            case ConversationStage.AwaitingNoteContent:
                if (string.IsNullOrEmpty(state.PendingNoteName))
                {
                    logger.LogError("Chat {ChatId} was awaiting note content with no pending note name; clearing state.", chatId);
                    stateStore.Clear(chatId);
                    return "Something went wrong with that note. Please start again with /new.";
                }

                var namedResult = await notePipeline.CreateNamedNoteAsync(state.PendingNoteName, messageText, cancellationToken);
                stateStore.Clear(chatId);
                return namedResult.Filename;

            default:
                var result = await notePipeline.CreateNoteAsync(messageText, cancellationToken);
                return result.Filename;
        }
    }

    private static string? ExtractCommand(string messageText)
    {
        var trimmed = messageText.Trim();
        if (trimmed.Length == 0 || trimmed[0] != '/')
        {
            return null;
        }

        var token = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
        var atIndex = token.IndexOf('@');
        return atIndex < 0 ? token : token[..atIndex];
    }
}
