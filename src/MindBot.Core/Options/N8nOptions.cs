namespace MindBot.Core.Options;

/// <summary>
/// Bound from the N8N__ environment variable prefix. Configures the self-hosted n8n instance whose
/// webhooks back the /ytsummary pipeline.
/// <para>
/// Unlike every other options class here, this one is entirely optional: a deployment without n8n
/// leaves <see cref="BaseUrl"/> empty and the bot starts normally, rejecting /ytsummary with an
/// explanation instead of refusing to boot.
/// </para>
/// </summary>
public sealed class N8nOptions
{
    public const string SectionName = "N8N";

    /// <summary>
    /// Base URL the webhook paths are appended to, e.g. "https://n8n.internal/webhook". Empty
    /// disables /ytsummary.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Per-request timeout. Generous by default: summarize-chunks fans out to an LLM once per
    /// chunk, so a long video legitimately takes minutes.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 600;

    /// <summary>How many times a summary job runs the whole pipeline before it is marked failed.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Base for the exponential backoff between those attempts.</summary>
    public int RetryBaseSeconds { get; set; } = 30;

    /// <summary>True when the pipeline is configured and /ytsummary can be accepted.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
}
