namespace MindBot.Core.Notes;

/// <summary>
/// Resolves a note filename against the vault root and verifies the resolved absolute path
/// is still inside that root. This is a defense-in-depth check performed even though the
/// filename has already been through <see cref="FilenameSanitiser"/>.
/// </summary>
public static class VaultPathResolver
{
    public static string ResolveNotePath(string vaultRoot, string filename)
    {
        var rootFull = Path.GetFullPath(vaultRoot);
        var candidate = Path.GetFullPath(Path.Combine(rootFull, filename));

        var rootWithSeparator = rootFull.EndsWith(Path.DirectorySeparatorChar)
            ? rootFull
            : rootFull + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Resolved note path '{candidate}' escapes vault root '{rootFull}'.");
        }

        return candidate;
    }
}
