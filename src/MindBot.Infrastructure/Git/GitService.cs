using MindBot.Core.Git;
using MindBot.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MindBot.Infrastructure.Git;

/// <summary>
/// Drives the `git` CLI as a subprocess (never LibGit2Sharp). Every operation is serialized
/// behind a single semaphore so the bot — the sole writer to its configured branch — never
/// runs two git invocations against the working tree concurrently.
/// </summary>
public sealed class GitService : IGitService
{
    private readonly GitOptions _gitOptions;
    private readonly VaultOptions _vaultOptions;
    private readonly ILogger<GitService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public GitService(IOptions<GitOptions> gitOptions, IOptions<VaultOptions> vaultOptions, ILogger<GitService> logger)
    {
        _gitOptions = gitOptions.Value;
        _vaultOptions = vaultOptions.Value;
        _logger = logger;
    }

    public async Task<GitOperationResult> EnsureRepositoryAsync(CancellationToken cancellationToken = default)
    {
        var isEmpty = !Directory.Exists(_vaultOptions.Root) || !Directory.EnumerateFileSystemEntries(_vaultOptions.Root).Any();

        if (isEmpty)
        {
            Directory.CreateDirectory(_vaultOptions.Root);

            var cloneResult = await RunGitInAsync(
                Path.GetTempPath(),
                ["clone", "--single-branch", _gitOptions.RemoteUrl, _vaultOptions.Root],
                cancellationToken);

            if (!cloneResult.Success)
            {
                _logger.LogError("git clone failed: {Error}", cloneResult.ErrorMessage);
                return cloneResult;
            }

            _logger.LogInformation("Cloned repository into {Root}.", _vaultOptions.Root);
        }

        var identityResult = await ConfigureLocalIdentityAsync(cancellationToken);
        if (!identityResult.Success)
        {
            return identityResult;
        }

        var fetchResult = await RunGitAsync(
            ["fetch", "origin", $"{_gitOptions.Branch}:refs/remotes/origin/{_gitOptions.Branch}"],
            cancellationToken);

        if (fetchResult.Success)
        {
            var checkoutResult = await RunGitAsync(["checkout", "-B", _gitOptions.Branch, $"origin/{_gitOptions.Branch}"], cancellationToken);
            if (!checkoutResult.Success)
            {
                return checkoutResult;
            }

            _logger.LogInformation("Checked out existing branch '{Branch}' from origin.", _gitOptions.Branch);
            return GitOperationResult.Ok;
        }

        var createResult = await RunGitAsync(["checkout", "-B", _gitOptions.Branch], cancellationToken);
        if (!createResult.Success)
        {
            return createResult;
        }

        var pushResult = await RunGitAsync(["push", "-u", "origin", _gitOptions.Branch], cancellationToken);
        if (!pushResult.Success)
        {
            return pushResult;
        }

        _logger.LogInformation(
            "Branch '{Branch}' did not exist on origin; created it from the default branch and pushed it.",
            _gitOptions.Branch);
        return GitOperationResult.Ok;
    }

    public Task<GitOperationResult> PullAsync(CancellationToken cancellationToken = default) =>
        RunGitAsync(["pull", "--rebase", "--autostash", "origin", _gitOptions.Branch], cancellationToken);

    public async Task<GitOperationResult> CommitAsync(string message, CancellationToken cancellationToken = default)
    {
        var addResult = await RunGitAsync(["add", "-A"], cancellationToken);
        if (!addResult.Success)
        {
            return addResult;
        }

        var commitResult = await RunGitAsync(["commit", "-m", message], cancellationToken);
        if (!commitResult.Success && commitResult.ErrorMessage?.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase) == true)
        {
            return GitOperationResult.Ok;
        }

        return commitResult;
    }

    public Task<GitOperationResult> PushAsync(CancellationToken cancellationToken = default) =>
        RunGitAsync(["push", "origin", _gitOptions.Branch], cancellationToken);

    public Task<GitOperationResult> VerifyRemoteWritableAsync(CancellationToken cancellationToken = default) =>
        RunGitAsync(["push", "--dry-run", "origin", _gitOptions.Branch], cancellationToken);

    private async Task<GitOperationResult> ConfigureLocalIdentityAsync(CancellationToken cancellationToken)
    {
        var nameResult = await RunGitAsync(["config", "--local", "user.name", _gitOptions.UserName], cancellationToken);
        if (!nameResult.Success)
        {
            return nameResult;
        }

        return await RunGitAsync(["config", "--local", "user.email", _gitOptions.UserEmail], cancellationToken);
    }

    private Dictionary<string, string?> BuildEnvironment()
    {
        var sshCommand = $"ssh -i {_gitOptions.SshKeyPath} -o StrictHostKeyChecking=yes";
        if (!string.IsNullOrWhiteSpace(_gitOptions.KnownHostsPath))
        {
            sshCommand += $" -o UserKnownHostsFile={_gitOptions.KnownHostsPath}";
        }

        return new Dictionary<string, string?> { ["GIT_SSH_COMMAND"] = sshCommand };
    }

    private Task<GitOperationResult> RunGitAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        RunGitInAsync(_vaultOptions.Root, arguments, cancellationToken);

    private async Task<GitOperationResult> RunGitInAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var result = await ProcessRunner.RunAsync("git", arguments, workingDirectory, BuildEnvironment(), cancellationToken);
            if (!result.Success)
            {
                _logger.LogWarning(
                    "git {Arguments} failed with exit code {ExitCode}: {Output}",
                    string.Join(' ', arguments),
                    result.ExitCode,
                    result.CombinedOutput);
                return GitOperationResult.Fail(result.CombinedOutput);
            }

            return GitOperationResult.Ok;
        }
        finally
        {
            _lock.Release();
        }
    }
}
