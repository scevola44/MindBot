namespace MindBot.Core.Notes;

public static class NoteFilenameFactory
{
    /// <summary>Builds a minute-precision {yyyyMMddHHmm}.md filename from the creation time.</summary>
    public static string CreateFromTimestamp(DateTimeOffset created) => $"{created:yyyyMMddHHmm}.md";

    /// <summary>Builds a filename from a user-supplied note name, sanitised to be filesystem-safe.</summary>
    public static string CreateFromName(string name) => $"{FilenameSanitiser.Sanitise(name)}.md";

    /// <summary>
    /// The <paramref name="attempt"/>-th candidate for a base filename: attempt 1 is the base
    /// itself, attempt 2 is "{stem}-2.md", and so on.
    /// <para>
    /// Minute-precision filenames collide whenever two messages arrive in the same minute, and a
    /// plain overwrite would destroy a note — which the vault invariants forbid. Suffixing keeps
    /// every capture while leaving the common single-message case with an unadorned name.
    /// </para>
    /// </summary>
    public static string CreateCandidate(string baseFilename, int attempt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);

        if (attempt == 1)
        {
            return baseFilename;
        }

        var extension = Path.GetExtension(baseFilename);
        var stem = Path.GetFileNameWithoutExtension(baseFilename);
        return $"{stem}-{attempt}{extension}";
    }
}
