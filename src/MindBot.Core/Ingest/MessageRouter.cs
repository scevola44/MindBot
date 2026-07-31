using MindBot.Core.Durability;
using MindBot.Core.Notes;
using Microsoft.Extensions.Logging;

namespace MindBot.Core.Ingest;

/// <summary>
/// Decides what an incoming, already-authorized text message means: a /new or /cancel command, a
/// reply within a pending /new conversation, or plain text to file as a timestamp-only fleeting
/// note. Returns the reply text to send back to the chat.
/// <para>
/// Every state change it makes goes through the supplied <see cref="IIngestUnitOfWork"/>, so
/// routing a message and durably queueing the resulting note commit or roll back together.
/// </para>
/// </summary>
public sealed class MessageRouter(NotePlanner notePlanner, ILogger<MessageRouter> logger)
{
    public async Task<string> RouteAsync(
        IIngestUnitOfWork unitOfWork,
        long updateId,
        long chatId,
        long senderId,
        string messageText,
        CancellationToken cancellationToken = default)
    {
        var command = ExtractCommand(messageText);

        if (string.Equals(command, "/cancel", StringComparison.OrdinalIgnoreCase))
        {
            var current = await unitOfWork.GetConversationAsync(chatId, cancellationToken);
            if (current.Stage == ConversationStage.None)
            {
                return "Nothing to cancel.";
            }

            await unitOfWork.ClearConversationAsync(chatId, cancellationToken);
            return "Cancelled.";
        }

        if (string.Equals(command, "/new", StringComparison.OrdinalIgnoreCase))
        {
            await unitOfWork.SetConversationAsync(chatId, new ConversationState(ConversationStage.AwaitingNoteName), cancellationToken);
            return "What would you like to name the note?";
        }

        var state = await unitOfWork.GetConversationAsync(chatId, cancellationToken);
        switch (state.Stage)
        {
            case ConversationStage.AwaitingNoteName:
                await unitOfWork.SetConversationAsync(
                    chatId,
                    state with { Stage = ConversationStage.AwaitingNoteContent, PendingNoteName = messageText.Trim() },
                    cancellationToken);
                return "Got it. Now send me the note content.";

            case ConversationStage.AwaitingNoteContent:
                if (string.IsNullOrEmpty(state.PendingNoteName))
                {
                    logger.LogError("Chat {ChatId} was awaiting note content with no pending note name; clearing state.", chatId);
                    await unitOfWork.ClearConversationAsync(chatId, cancellationToken);
                    return "Something went wrong with that note. Please start again with /new.";
                }

                var named = notePlanner.PlanNamedNote(state.PendingNoteName, messageText);
                await unitOfWork.ClearConversationAsync(chatId, cancellationToken);
                return await QueueAsync(unitOfWork, updateId, chatId, senderId, named, cancellationToken);

            default:
                var quick = notePlanner.PlanQuickNote(messageText);
                return await QueueAsync(unitOfWork, updateId, chatId, senderId, quick, cancellationToken);
        }
    }

    private static async Task<string> QueueAsync(
        IIngestUnitOfWork unitOfWork,
        long updateId,
        long chatId,
        long senderId,
        NoteDraft draft,
        CancellationToken cancellationToken)
    {
        var filename = await unitOfWork.ReserveFilenameAsync(draft.BaseFilename, cancellationToken);
        await unitOfWork.EnqueueWriteJobAsync(updateId, filename, draft.Content, chatId, senderId, cancellationToken);
        return filename;
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
