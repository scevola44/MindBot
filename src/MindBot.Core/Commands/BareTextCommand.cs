using MindBot.Core.Notes;
using MindBot.Core.Operations;

namespace MindBot.Core.Commands;

/// <summary>
/// The catch-all for plain text: one <see cref="CreateNote"/> in the fleeting folder, identical to
/// what NotePlanner.PlanQuickNote + MessageRouter.QueueAsync produced before this migration. Must
/// be registered last in <see cref="CommandDispatcher"/> since it matches unconditionally.
/// </summary>
public sealed class BareTextCommand(TimeProvider timeProvider) : ICommand
{
    public bool Matches(string messageText) => true;

    public async Task<CommandResult> HandleAsync(string messageText, IVaultOperationContext context, CancellationToken cancellationToken = default)
    {
        var created = timeProvider.GetLocalNow();
        var baseFilename = NoteFilenameFactory.CreateFromTimestamp(created);
        var filename = await context.ReserveFilenameAsync(baseFilename, cancellationToken);
        var path = VaultLayout.RelativeNotePath(filename);

        var frontmatter = new NoteFrontmatter { Date = created.ToString("yyyy-MM-ddTHH:mm:sszzz") };
        var body = WikilinkTransformer.Transform(messageText);

        var operation = new CreateNote(path, frontmatter, body);
        return new CommandResult.Operations([operation], $"Add note {filename}", filename);
    }
}
