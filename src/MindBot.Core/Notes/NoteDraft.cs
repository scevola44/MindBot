namespace MindBot.Core.Notes;

/// <summary>
/// A note's content plus the filename it would prefer. The filename is a <em>candidate</em>: the
/// ingest transaction resolves it against existing notes and already-queued jobs before it
/// becomes final, so a burst of messages within the same minute cannot overwrite each other.
/// </summary>
public sealed record NoteDraft(string BaseFilename, string Content);
