namespace MindBot.Core.Options;

/// <summary>Bound from the WEB__ environment variable prefix. Configures the self-hosted chat API.</summary>
public sealed class WebOptions
{
    public const string SectionName = "WEB";

    /// <summary>The health endpoint's port. The web API must never share it: /health stays in-container.</summary>
    public const int HealthPort = 8080;

    /// <summary>Off by default, so an existing Telegram-only deployment opens no new port on upgrade.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// In-container port for the chat API and the web client. Publish it to the host's loopback
    /// only and expose it through the VPN (e.g. <c>tailscale serve</c>); never to the internet.
    /// </summary>
    public int Port { get; set; } = 8081;

    /// <summary>
    /// Optional directory holding the exported web client (index.html and its assets). Served on
    /// <see cref="Port"/> when set and present; the API works without it.
    /// </summary>
    public string? StaticRoot { get; set; }

    /// <summary>
    /// How long the web transcript is kept. Replies can quote vault content (/preview), so the
    /// history is not kept forever by default.
    /// </summary>
    public int HistoryRetentionDays { get; set; } = 30;

    /// <summary>Longest accepted message, in characters. Telegram's own limit is 4096.</summary>
    public int MaxMessageLength { get; set; } = 16_000;
}
