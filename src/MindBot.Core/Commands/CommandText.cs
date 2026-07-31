namespace MindBot.Core.Commands;

/// <summary>Shared slash-command text parsing for the commands in this folder. Mirrors MessageRouter's own ExtractCommand exactly, including "/cmd@BotName" stripping.</summary>
internal static class CommandText
{
    public static string? ExtractCommand(string messageText)
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

    /// <summary>Everything after the command token and its following whitespace, trimmed.</summary>
    public static string ExtractArgument(string messageText)
    {
        var trimmed = messageText.Trim();
        var spaceIndex = trimmed.IndexOfAny([' ', '\t']);
        return spaceIndex < 0 ? string.Empty : trimmed[(spaceIndex + 1)..].Trim();
    }
}
