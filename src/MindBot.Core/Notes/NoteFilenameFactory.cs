namespace MindBot.Core.Notes;

public static class NoteFilenameFactory
{
    /// <summary>Builds a {yyyy-MM-dd}T{HHmmss}-{slug}.md filename from the message and its creation time.</summary>
    public static string Create(DateTimeOffset created, string messageText)
    {
        var timestamp = created.ToString("yyyy-MM-ddTHHmmss");
        var slug = FilenameSanitiser.SlugFromText(messageText);
        return $"{timestamp}-{slug}.md";
    }
}
