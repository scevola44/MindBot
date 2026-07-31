using MindBot.Core.Notes;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MindBot.Core.Operations;

/// <summary>
/// Resolves <see cref="CreateNote"/>. Deliberately duplicates NoteContentBuilder's serializer
/// config rather than sharing it, so NoteContentBuilder -- still used untouched by the legacy /new
/// path -- is never at risk of being changed by this migration. Keep the two in sync if that
/// config ever changes.
/// </summary>
public sealed class CreateNoteHandler : IVaultOperationHandler
{
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public bool CanHandle(IVaultOperation operation) => operation is CreateNote;

    public Task<ResolvedWrite> ResolveAsync(IVaultOperation operation, IVaultOperationContext context, CancellationToken cancellationToken = default)
    {
        var note = (CreateNote)operation;

        // Defense-in-depth: the path already went through FilenameSanitiser/reservation upstream,
        // but this is the check that also protects the /preview scratch path, which never reaches
        // VaultNoteWriter (the component that would otherwise be the only thing enforcing this).
        VaultPathResolver.ResolveNotePath(context.VaultRoot, note.Path);

        var yaml = Serializer.Serialize(note.Frontmatter);
        var content = $"---\n{yaml}---\n\n{note.Body}\n";

        var relativeFolder = Path.GetDirectoryName(note.Path) ?? string.Empty;
        var filename = Path.GetFileName(note.Path);
        return Task.FromResult(new ResolvedWrite(relativeFolder, filename, content));
    }
}
