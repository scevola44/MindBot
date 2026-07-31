namespace MindBot.Core.Notes;

/// <summary>
/// Where notes live inside the vault. Shared by the writer and by filename reservation, which
/// must probe the same directory the writer will write to.
/// </summary>
public static class VaultLayout
{
    public const string FleetingFolder = "05 - Fleeting";

    /// <summary>Vault-root-relative path for a note filename, using the platform separator.</summary>
    public static string RelativeNotePath(string filename) => Path.Combine(FleetingFolder, filename);
}
