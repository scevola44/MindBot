namespace MindBot.Core.Operations;

/// <summary>
/// Everything an operation handler needs to resolve an operation into content, without ever
/// knowing about Telegram, git, or the SQLite ingest transaction. Mirrors
/// <c>IIngestUnitOfWork.GetLatestNoteContentAsync</c>/<c>ReserveFilenameAsync</c> exactly, so real
/// command handling and /preview can share the same handler code against different backing stores.
/// </summary>
public interface IVaultOperationContext
{
    string VaultRoot { get; }

    /// <summary>
    /// The most up-to-date content for a note at <paramref name="relativeFolder"/>/<paramref name="filename"/>,
    /// or null if it does not exist yet.
    /// </summary>
    Task<string?> GetLatestContentAsync(string relativeFolder, string filename, CancellationToken cancellationToken = default);

    /// <summary>Resolves <paramref name="baseFilename"/> to a name that collides with nothing already claimed.</summary>
    Task<string> ReserveFilenameAsync(string baseFilename, CancellationToken cancellationToken = default);
}
