namespace MindBot.Core.Commands;

public sealed record CommandDescriptor(string Name, string Usage, string Description);

/// <summary>
/// What the chat clients show for "/" autocomplete. Kept by hand because two kinds of command
/// exist: MessageRouter's built-ins (/new, /cancel, /task, /todo) and DI-discovered
/// <see cref="ICommand"/>s, which carry no metadata. When adding a command, add it here too;
/// CommandCatalogTests fails if an ICommand's name is missing.
/// </summary>
public static class CommandCatalog
{
    public const string YouTubeSummary = "/ytsummary";

    public static IReadOnlyList<CommandDescriptor> All { get; } =
    [
        new("/new", "/new", "Start a named note: the next message is the name, the one after is the content."),
        new("/cancel", "/cancel", "Abandon a /new in progress."),
        new("/task", "/task <item>", "Add an item to today's task note. One item per line adds several."),
        new("/todo", "/todo <item>", "Same as /task."),
        new("/append", "/append <text>", "Append text to Scratch.md."),
        new("/preview", "/preview <command...>", "Show what a command would write, without writing it."),
        new("/status", "/status", "Show the bot's health: last push, queue depth, unpushed commits."),
        new(YouTubeSummary, "/ytsummary <youtube-url> [chunks]", "Summarise a YouTube video into a note. The reply arrives when it is done."),
    ];
}
