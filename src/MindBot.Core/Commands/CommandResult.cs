using MindBot.Core.Operations;

namespace MindBot.Core.Commands;

/// <summary>What handling a message produced. Exactly one of three shapes -- a status-style command with nothing to write is just <see cref="DirectReply"/>, never an awkward empty operation list.</summary>
public abstract record CommandResult
{
    private CommandResult()
    {
    }

    /// <summary>
    /// One or more vault operations to resolve and enqueue, plus the commit message the command
    /// would like used, and the reply to send once queued. <see cref="Reply"/> is kept separate
    /// from <see cref="CommitMessage"/> because bare text's existing observable reply (the bare
    /// filename) differs from a sensible commit message -- keeping them distinct is what makes the
    /// bare-text migration byte-identical without leaking per-command reply formatting into the
    /// generic executor.
    /// </summary>
    public sealed record Operations(IReadOnlyList<IVaultOperation> Items, string CommitMessage, string Reply) : CommandResult;

    /// <summary>
    /// Work too slow to do inside the ingest transaction, recorded durably for a background worker.
    /// <paramref name="Kind"/> selects the worker and <paramref name="Payload"/> is its JSON input;
    /// the executor stores both without interpreting either.
    /// <para>
    /// This exists because <see cref="Operations"/> cannot express it: an operation is resolved to
    /// its final bytes during ingest, and a /ytsummary note's content is not known until five n8n
    /// webhooks — minutes of network and LLM time — have run.
    /// </para>
    /// </summary>
    public sealed record DeferredJob(string Kind, string Payload, string Reply) : CommandResult;

    /// <summary>A text reply with no vault write at all.</summary>
    public sealed record DirectReply(string Text) : CommandResult;

    /// <summary>Malformed input; <paramref name="Reason"/> is shown back to the user.</summary>
    public sealed record Rejected(string Reason) : CommandResult;
}
