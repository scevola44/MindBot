namespace MindBot.Core.Operations;

/// <summary>Creates a new note at <paramref name="Path"/> (vault-root-relative, already reservation-resolved) with the given frontmatter object and body.</summary>
public sealed record CreateNote(string Path, object Frontmatter, string Body) : IVaultOperation;
