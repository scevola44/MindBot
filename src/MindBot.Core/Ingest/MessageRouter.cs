using MindBot.Core.Commands;
using MindBot.Core.Durability;
using MindBot.Core.Notes;
using Microsoft.Extensions.Logging;

namespace MindBot.Core.Ingest;

/// <summary>
/// Decides what an incoming, already-authorized text message means: a /new or /cancel command, a
/// reply within a pending /new conversation, or (via <see cref="CommandExecutor"/>) a DI-discovered
/// command -- including plain text, which files as a timestamp-only fleeting note. Returns the
/// reply text to send back to the chat.
/// <para>
/// Every state change it makes goes through the supplied <see cref="IIngestUnitOfWork"/>, so
/// routing a message and durably queueing the resulting note commit or roll back together.
/// </para>
/// </summary>
public sealed class MessageRouter(NotePlanner notePlanner, TimeProvider timeProvider, ILogger<MessageRouter> logger, CommandExecutor commandExecutor)
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

        if (string.Equals(command, "/task", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "/todo", StringComparison.OrdinalIgnoreCase))
        {
            return await HandleTaskCommandAsync(unitOfWork, updateId, chatId, senderId, messageText, cancellationToken);
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
                return await commandExecutor.ExecuteAsync(unitOfWork, updateId, chatId, senderId, messageText, cancellationToken);
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
        await unitOfWork.EnqueueWriteJobAsync(updateId, VaultLayout.FleetingFolder, filename, draft.Content, chatId, senderId, cancellationToken);
        return filename;
    }

    private async Task<string> HandleTaskCommandAsync(
        IIngestUnitOfWork unitOfWork,
        long updateId,
        long chatId,
        long senderId,
        string messageText,
        CancellationToken cancellationToken)
    {
        var items = ExtractTaskItems(messageText);
        if (items.Count == 0)
        {
            return "Usage: /task <item> (send one item per line to add several at once).";
        }

        var now = timeProvider.GetLocalNow();
        var date = DateOnly.FromDateTime(now.DateTime);
        var folder = VaultLayout.TaskNoteFolder(date);
        var filename = VaultLayout.TaskNoteFilename(date);

        var existingContent = await unitOfWork.GetLatestNoteContentAsync(folder, filename, cancellationToken);
        var content = TaskNoteContentBuilder.Append(existingContent, items, now);

        await unitOfWork.EnqueueWriteJobAsync(updateId, folder, filename, content, chatId, senderId, cancellationToken);

        return items.Count == 1
            ? $"Added to {filename}."
            : $"Added {items.Count} items to {filename}.";
    }

    /// <summary>
    /// The first line's remainder after the /task or /todo token (if any) is the first item; every
    /// other non-empty line is another item, letting a single message file several tasks at once.
    /// </summary>
    private static IReadOnlyList<string> ExtractTaskItems(string messageText)
    {
        var lines = messageText.Replace("\r\n", "\n").Split('\n');
        var items = new List<string>();

        var firstLine = lines[0].TrimStart();
        var spaceIndex = firstLine.IndexOfAny([' ', '\t']);
        var remainder = spaceIndex < 0 ? string.Empty : firstLine[(spaceIndex + 1)..].Trim();
        if (remainder.Length > 0)
        {
            items.Add(remainder);
        }

        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length > 0)
            {
                items.Add(line);
            }
        }

        return items;
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
