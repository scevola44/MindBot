using MindBot.Core.Options;
using MindBot.Core.Web;
using MindBot.Infrastructure.State;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MindBot.Bot.Web;

/// <summary>
/// <c>dotnet MindBot.Bot.dll device add|list|revoke</c>, run inside the container with
/// <c>docker compose exec mindbot ...</c>. It needs only the state database, so it starts without
/// Telegram, git or vault configuration and never touches the running bot.
/// </summary>
public static class DeviceCli
{
    public const string Verb = "device";

    private const string Usage = """
        Usage:
          device add <name>      Create a device and print its token (shown once).
          device list            List devices.
          device revoke <name>   Revoke a device; its token stops working immediately.
        """;

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, IConfiguration? configuration = null)
    {
        configuration ??= new ConfigurationBuilder().AddEnvironmentVariables().Build();

        var stateOptions = new StateOptions();
        configuration.GetSection(StateOptions.SectionName).Bind(stateOptions);
        if (string.IsNullOrWhiteSpace(stateOptions.DatabasePath) || !Path.IsPathRooted(stateOptions.DatabasePath))
        {
            await error.WriteLineAsync("STATE__DATABASEPATH must be set to the bot's database (an absolute path).");
            return 2;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(stateOptions));
        services.AddSingleton(Options.Create(new VaultOptions()));
        services.AddSingleton(Options.Create(new WebOptions()));
        services.AddMindBotState();
        await using var provider = services.BuildServiceProvider();

        // Same as the bot's own startup; a no-op when it has already run.
        Directory.CreateDirectory(Path.GetDirectoryName(stateOptions.DatabasePath)!);
        await using (var db = await provider.GetRequiredService<IDbContextFactory<MindBotDbContext>>().CreateDbContextAsync())
        {
            await db.Database.MigrateAsync();
        }

        var store = provider.GetRequiredService<IDeviceTokenStore>();

        switch (args)
        {
            case ["add", var name] when !string.IsNullOrWhiteSpace(name):
                try
                {
                    var (device, token) = await store.CreateAsync(name);
                    await output.WriteLineAsync($"Created device '{device.Name}'. Its token, shown only now:");
                    await output.WriteLineAsync();
                    await output.WriteLineAsync($"  {token}");
                    await output.WriteLineAsync();
                    await output.WriteLineAsync("Paste it into the client's setup screen. Revoke with: device revoke " + device.Name);
                    return 0;
                }
                catch (InvalidOperationException ex)
                {
                    await error.WriteLineAsync(ex.Message);
                    return 1;
                }

            case ["list"]:
                var devices = await store.ListAsync();
                if (devices.Count == 0)
                {
                    await output.WriteLineAsync("No devices.");
                }

                foreach (var d in devices)
                {
                    var state = d.IsRevoked ? $"revoked {d.RevokedAt:u}" : "active";
                    await output.WriteLineAsync($"{d.Name,-20} {state,-28} created {d.CreatedAt:u}  last seen {(d.LastSeenAt is { } seen ? seen.ToString("u") : "never")}");
                }

                return 0;

            case ["revoke", var name]:
                if (await store.RevokeAsync(name))
                {
                    await output.WriteLineAsync($"Revoked '{name}'.");
                    return 0;
                }

                await error.WriteLineAsync($"No active device named '{name}'.");
                return 1;

            default:
                await error.WriteLineAsync(Usage);
                return 2;
        }
    }
}
