namespace MindBot.Tests.Fakes;

/// <summary>A real, disposable temp directory standing in for VAULT__ROOT, for tests that need actual filesystem reads/writes.</summary>
public sealed class TempVaultRoot : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("mindbot-vault-").FullName;

    public void WriteFile(string relativeFolder, string filename, string content)
    {
        var dir = System.IO.Path.Combine(Path, relativeFolder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(System.IO.Path.Combine(dir, filename), content);
    }

    public string? ReadFile(string relativeFolder, string filename)
    {
        var file = System.IO.Path.Combine(Path, relativeFolder, filename);
        return File.Exists(file) ? File.ReadAllText(file) : null;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup; not worth failing a test over.
        }
    }
}
