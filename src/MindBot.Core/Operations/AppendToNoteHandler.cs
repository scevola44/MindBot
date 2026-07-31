using MindBot.Core.Notes;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MindBot.Core.Operations;

/// <summary>
/// Resolves <see cref="AppendToNote"/>. The frontmatter block of an existing note is never parsed
/// into an object and re-emitted -- it is carried forward as a verbatim substring
/// (<see cref="NoteFrontmatterSplitter"/>), which is what guarantees unknown keys, key order, and
/// comments survive completely untouched. YamlDotNet is used only to validate that the block
/// parses as YAML at all, and the parsed value is discarded.
/// </summary>
public sealed class AppendToNoteHandler(TimeProvider timeProvider) : IVaultOperationHandler
{
    private sealed record MinimalFrontmatter(string Date);

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private static readonly IDeserializer SyntaxValidator = new DeserializerBuilder().Build();

    public bool CanHandle(IVaultOperation operation) => operation is AppendToNote;

    public async Task<ResolvedWrite> ResolveAsync(IVaultOperation operation, IVaultOperationContext context, CancellationToken cancellationToken = default)
    {
        var op = (AppendToNote)operation;

        // Defense-in-depth: this is the only enforcement point on the /preview scratch path, which
        // never reaches VaultNoteWriter.
        VaultPathResolver.ResolveNotePath(context.VaultRoot, op.Path);

        var relativeFolder = Path.GetDirectoryName(op.Path) ?? string.Empty;
        var filename = Path.GetFileName(op.Path);

        var existing = await context.GetLatestContentAsync(relativeFolder, filename, cancellationToken);
        if (existing is null)
        {
            return new ResolvedWrite(relativeFolder, filename, WithFreshFrontmatter(body: null, op.Content));
        }

        var split = NoteFrontmatterSplitter.Split(existing);
        if (split is null)
        {
            // Not our shape: keep the whole existing content as body, same fallback
            // TaskNoteContentBuilder.Parse uses for its own not-our-shape case.
            return new ResolvedWrite(relativeFolder, filename, WithFreshFrontmatter(existing, op.Content));
        }

        var (frontmatterBlockVerbatim, body) = split.Value;
        try
        {
            // Discarded: this call exists only to detect malformed YAML, never to reconstruct output from it.
            SyntaxValidator.Deserialize<object>(NoteFrontmatterSplitter.InnerYaml(frontmatterBlockVerbatim));
        }
        catch (YamlException ex)
        {
            // Silently treating this as "no frontmatter" would leave the original "---...---" bytes
            // sitting inside a new body while a second, fresh frontmatter block gets prepended on
            // top of it -- silent corruption, strictly worse than refusing the append.
            throw new VaultOperationException($"Frontmatter in '{op.Path}' is not valid YAML; refusing to append. {ex.Message}");
        }

        var content = frontmatterBlockVerbatim + AppendToBody(body, op.Content);
        return new ResolvedWrite(relativeFolder, filename, content);
    }

    private string WithFreshFrontmatter(string? body, string appended)
    {
        var date = timeProvider.GetLocalNow().ToString("yyyy-MM-ddTHH:mm:sszzz");
        var yaml = Serializer.Serialize(new MinimalFrontmatter(date));
        return $"---\n{yaml}---\n\n{AppendToBody(body ?? string.Empty, appended)}";
    }

    private static string AppendToBody(string body, string appended)
    {
        var separator = body.Length == 0 || body.EndsWith('\n') ? string.Empty : "\n";
        return body + separator + appended.TrimEnd('\n') + "\n";
    }
}
