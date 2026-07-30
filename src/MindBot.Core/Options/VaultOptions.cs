namespace MindBot.Core.Options;

/// <summary>Bound from the VAULT__ environment variable prefix.</summary>
public sealed class VaultOptions
{
    public const string SectionName = "VAULT";

    /// <summary>Absolute path to the local clone of the vault repository (typically a mounted named volume).</summary>
    public string Root { get; set; } = string.Empty;
}
