using MindBot.Core.Git;
using MindBot.Core.Options;
using Microsoft.Extensions.Options;

namespace MindBot.Bot.Services;

/// <summary>
/// Verifies the vault repository is cloned/checked out and the remote branch is writable
/// before the Telegram poller starts accepting updates. Registered before the polling hosted
/// service so a misconfigured deploy fails startup visibly instead of silently dropping messages.
/// </summary>
public sealed class GitStartupCheckHostedService(
    IGitService gitService,
    IOptions<GitOptions> gitOptions,
    IOptions<VaultOptions> vaultOptions,
    ILogger<GitStartupCheckHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Running git startup self-check against {Root}...", vaultOptions.Value.Root);

        var ensureResult = await gitService.EnsureRepositoryAsync(cancellationToken);
        if (!ensureResult.Success)
        {
            throw new InvalidOperationException(
                $"MindBot startup self-check failed: unable to prepare the vault repository at '{vaultOptions.Value.Root}': {ensureResult.ErrorMessage}");
        }

        var writableResult = await gitService.VerifyRemoteWritableAsync(cancellationToken);
        if (!writableResult.Success)
        {
            throw new InvalidOperationException(
                $"MindBot startup self-check failed: branch '{gitOptions.Value.Branch}' is not writable on the remote: {writableResult.ErrorMessage}");
        }

        logger.LogInformation("Git startup self-check passed; vault ready on branch '{Branch}'.", gitOptions.Value.Branch);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
