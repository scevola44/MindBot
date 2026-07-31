namespace MindBot.Core.Operations;

/// <summary>Appends <paramref name="Content"/> to the body of the note at <paramref name="Path"/> (vault-root-relative), creating it with minimal frontmatter if it doesn't exist yet.</summary>
public sealed record AppendToNote(string Path, string Content) : IVaultOperation;
