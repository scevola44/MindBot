using MindBot.Core.Notes;
using MindBot.Core.Options;
using Microsoft.Extensions.Options;

namespace MindBot.Infrastructure.Vault;

public sealed class VaultNoteWriter(IOptions<VaultOptions> vaultOptions) : IVaultWriter
{
    public async Task<string> WriteNoteAsync(string relativeFolder, string filename, string content, CancellationToken cancellationToken = default)
    {
        var relativePath = Path.Combine(relativeFolder, filename);
        var path = VaultPathResolver.ResolveNotePath(vaultOptions.Value.Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, cancellationToken);
        return path;
    }
}
