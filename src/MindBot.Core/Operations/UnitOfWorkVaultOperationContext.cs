using MindBot.Core.Durability;

namespace MindBot.Core.Operations;

/// <summary>
/// The real <see cref="IVaultOperationContext"/>, used while handling an actual Telegram message.
/// A thin adapter over the ingest transaction's unit of work -- this is the seam that keeps
/// operation handlers free of any <see cref="IIngestUnitOfWork"/>/git/Telegram awareness while
/// still reusing the exact reservation/dedupe logic the /task command already depends on.
/// </summary>
public sealed class UnitOfWorkVaultOperationContext(IIngestUnitOfWork unitOfWork, string vaultRoot) : IVaultOperationContext
{
    public string VaultRoot => vaultRoot;

    public Task<string?> GetLatestContentAsync(string relativeFolder, string filename, CancellationToken cancellationToken = default) =>
        unitOfWork.GetLatestNoteContentAsync(relativeFolder, filename, cancellationToken);

    public Task<string> ReserveFilenameAsync(string baseFilename, CancellationToken cancellationToken = default) =>
        unitOfWork.ReserveFilenameAsync(baseFilename, cancellationToken);
}
