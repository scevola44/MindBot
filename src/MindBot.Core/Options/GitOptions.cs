namespace MindBot.Core.Options;

/// <summary>Bound from the GIT__ environment variable prefix.</summary>
public sealed class GitOptions
{
    public const string SectionName = "GIT";

    public string RemoteUrl { get; set; } = string.Empty;

    /// <summary>The bot-owned branch it exclusively reads from and writes to (e.g. bot-inbox).</summary>
    public string Branch { get; set; } = string.Empty;

    /// <summary>Path to the mounted SSH private key used to authenticate against the remote.</summary>
    public string SshKeyPath { get; set; } = string.Empty;

    /// <summary>Path to a known_hosts file. Falls back to ssh's default if not set.</summary>
    public string? KnownHostsPath { get; set; }

    public string UserName { get; set; } = "MindBot";

    public string UserEmail { get; set; } = "mindbot@localhost";
}
