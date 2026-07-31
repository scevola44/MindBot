namespace MindBot.Core.Notes;

/// <summary>
/// Turns message text into the exact note content and candidate filename to persist.
/// <para>
/// Deliberately does no I/O: planning happens inside the ingest transaction, where a git or
/// filesystem call would hold a SQLite write lock open across a network round trip. The actual
/// writing, committing and pushing is the drain worker's job.
/// </para>
/// </summary>
public sealed class NotePlanner(TimeProvider timeProvider)
{
    public NoteDraft PlanNamedNote(string name, string content)
    {
        var created = timeProvider.GetLocalNow();
        return new NoteDraft(
            NoteFilenameFactory.CreateFromName(name),
            NoteContentBuilder.Build(content, created));
    }
}
