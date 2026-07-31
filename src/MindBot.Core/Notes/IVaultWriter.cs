namespace MindBot.Core.Notes;

public interface IVaultWriter
{
    /// <summary>Writes note content to <paramref name="relativeFolder"/>/<paramref name="filename"/> inside the vault root and returns the full path written.</summary>
    Task<string> WriteNoteAsync(string relativeFolder, string filename, string content, CancellationToken cancellationToken = default);
}
