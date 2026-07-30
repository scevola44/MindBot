using MindBot.Core.Notes;
using MindBot.Core.Options;
using Microsoft.Extensions.Options;

namespace MindBot.Infrastructure.Vault;

public sealed class VaultNoteWriter(IOptions<VaultOptions> vaultOptions) : IVaultWriter
{
    public async Task<string> WriteNoteAsync(string filename, string content, CancellationToken cancellationToken = default)
    {
        var path = VaultPathResolver.ResolveNotePath(vaultOptions.Value.Root, filename);
        await File.WriteAllTextAsync(path, content, cancellationToken);
        return path;
    }
}
