namespace MindBot.Core.Notes;

public static class NoteFilenameFactory
{
    /// <summary>Builds a minute-precision {yyyyMMddHHmm}.md filename from the creation time.</summary>
    public static string CreateFromTimestamp(DateTimeOffset created) => $"{created:yyyyMMddHHmm}.md";

    /// <summary>Builds a filename from a user-supplied note name, sanitised to be filesystem-safe.</summary>
    public static string CreateFromName(string name) => $"{FilenameSanitiser.Sanitise(name)}.md";
}
