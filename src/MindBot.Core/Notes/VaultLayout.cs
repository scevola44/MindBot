using System.Globalization;

namespace MindBot.Core.Notes;

/// <summary>
/// Where notes live inside the vault. Shared by the writer and by filename reservation, which
/// must probe the same directory the writer will write to.
/// </summary>
public static class VaultLayout
{
    public const string FleetingFolder = "05 - Fleeting";

    public const string DailyNotesFolder = "06 - Daily Notes";

    /// <summary>Vault-root-relative path for a note filename, using the platform separator.</summary>
    public static string RelativeNotePath(string filename) => Path.Combine(FleetingFolder, filename);

    /// <summary>The year/month folder a daily task note for <paramref name="date"/> lives in, e.g. "06 - Daily Notes/2026/07 - July".</summary>
    public static string TaskNoteFolder(DateOnly date) => Path.Combine(
        DailyNotesFolder,
        date.Year.ToString(CultureInfo.InvariantCulture),
        $"{date.Month:D2} - {date.ToString("MMMM", CultureInfo.InvariantCulture)}");

    /// <summary>The filename of the single daily task note for <paramref name="date"/>.</summary>
    public static string TaskNoteFilename(DateOnly date) => $"TODO - {date:yyyy-MM-dd}.md";
}
