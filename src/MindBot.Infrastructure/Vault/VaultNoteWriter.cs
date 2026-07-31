using MindBot.Core.Notes;
using MindBot.Core.Options;
using Microsoft.Extensions.Options;

namespace MindBot.Infrastructure.Vault;

public sealed class VaultNoteWriter(IOptions<VaultOptions> vaultOptions) : IVaultWriter
{
    private const string FleetingFolder = "05 - Fleeting";

    public async Task<string> WriteNoteAsync(string filename, string content, CancellationToken cancellationToken = default)
    {
        var relativePath = Path.Combine(FleetingFolder, filename);
        var path = VaultPathResolver.ResolveNotePath(vaultOptions.Value.Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, cancellationToken);
        return path;
    }
}
