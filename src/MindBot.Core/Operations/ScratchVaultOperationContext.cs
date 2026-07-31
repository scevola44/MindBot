using MindBot.Core.Notes;

namespace MindBot.Core.Operations;

/// <summary>
/// The /preview <see cref="IVaultOperationContext"/>. Reads real files read-only, to seed the
/// "current content" a preview starts from, and keeps every resolved write in an in-memory overlay
/// -- it never writes to the real vault, the write-job queue, or git.
/// </summary>
public sealed class ScratchVaultOperationContext(string vaultRoot) : IVaultOperationContext
{
    private readonly Dictionary<(string Folder, string Filename), string> _overlay = [];
    private readonly HashSet<string> _reserved = [];

    public string VaultRoot => vaultRoot;

    public Task<string?> GetLatestContentAsync(string relativeFolder, string filename, CancellationToken cancellationToken = default)
    {
        if (_overlay.TryGetValue((relativeFolder, filename), out var overlaid))
        {
            return Task.FromResult<string?>(overlaid);
        }

        var path = Path.Combine(vaultRoot, relativeFolder, filename);
        return Task.FromResult<string?>(File.Exists(path) ? File.ReadAllText(path) : null);
    }

    public Task<string> ReserveFilenameAsync(string baseFilename, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            var candidate = NoteFilenameFactory.CreateCandidate(baseFilename, attempt);

            if (File.Exists(Path.Combine(vaultRoot, VaultLayout.RelativeNotePath(candidate))))
            {
                continue;
            }

            if (_reserved.Add(candidate))
            {
                return Task.FromResult(candidate);
            }
        }
    }

    /// <summary>Overlays a just-resolved write so a later operation in the same preview session sees it instead of stale disk content.</summary>
    public void RecordResolved(ResolvedWrite write) => _overlay[(write.RelativeFolder, write.Filename)] = write.Content;
}
