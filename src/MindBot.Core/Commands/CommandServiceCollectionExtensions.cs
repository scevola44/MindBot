using MindBot.Core.Health;
using MindBot.Core.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace MindBot.Core.Commands;

public static class CommandServiceCollectionExtensions
{
    /// <summary>
    /// Registers the command/operation architecture. Adding a future operation type or command
    /// requires only its own file plus one more line here -- never an edit to
    /// <see cref="VaultOperationApplier"/>, <see cref="CommandDispatcher"/>, or
    /// <see cref="CommandExecutor"/>.
    /// </summary>
    public static IServiceCollection AddMindBotCommands(this IServiceCollection services)
    {
        services.AddSingleton<VaultOperationApplier>();
        services.AddSingleton<IVaultOperationHandler, CreateNoteHandler>();
        services.AddSingleton<IVaultOperationHandler, AppendToNoteHandler>();

        services.AddSingleton<HealthReportService>();

        // Registration order matters: BareTextCommand is the unconditional catch-all and must be last.
        services.AddSingleton<ICommand, AppendCommand>();
        services.AddSingleton<ICommand, StatusCommand>();
        services.AddSingleton<ICommand, PreviewCommand>();
        services.AddSingleton<ICommand, BareTextCommand>();

        services.AddSingleton<CommandDispatcher>();
        services.AddSingleton<CommandExecutor>();

        return services;
    }
}
