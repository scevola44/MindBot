namespace MindBot.Core.Options;

/// <summary>
/// Shared containment check used by the options validators. The bot's own state (the SQLite
/// database, recovery bundles) must never live inside the vault working tree: <c>git add -A</c>
/// would sweep it into a commit and push it onto the operator's branch.
/// </summary>
public static class PathContainment
{
    public static bool IsInside(string candidate, string root)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        string candidateFull;
        string rootFull;
        try
        {
            candidateFull = Path.GetFullPath(candidate);
            rootFull = Path.GetFullPath(root);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // A malformed path cannot be proven to be outside the vault, so treat it as a conflict
            // and let the validator surface it.
            return true;
        }

        if (string.Equals(candidateFull, rootFull, StringComparison.Ordinal))
        {
            return true;
        }

        var rootWithSeparator = rootFull.EndsWith(Path.DirectorySeparatorChar)
            ? rootFull
            : rootFull + Path.DirectorySeparatorChar;

        return candidateFull.StartsWith(rootWithSeparator, StringComparison.Ordinal);
    }
}
