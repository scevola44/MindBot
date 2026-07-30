namespace MindBot.Core.Notes;

public interface IVaultWriter
{
    /// <summary>Writes note content to the given filename inside the vault root and returns the full path written.</summary>
    Task<string> WriteNoteAsync(string filename, string content, CancellationToken cancellationToken = default);
}
