using MindBot.Core.Git;
using MindBot.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MindBot.Infrastructure.Git;

/// <summary>
/// Drives the `git` CLI as a subprocess (never LibGit2Sharp).
/// <para>
/// The lock is taken once per <em>logical</em> operation rather than once per git invocation.
/// Classification followed by a pull is only meaningful if nothing can run between them, so the
/// public methods acquire the lock and the private core helpers assume it is already held.
/// </para>
/// </summary>
public sealed class GitService : IGitService
{
    private readonly GitOptions _gitOptions;
    private readonly VaultOptions _vaultOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GitService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public GitService(
        IOptions<GitOptions> gitOptions,
        IOptions<VaultOptions> vaultOptions,
        TimeProvider timeProvider,
        ILogger<GitService> logger)
    {
        _gitOptions = gitOptions.Value;
        _vaultOptions = vaultOptions.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<GitOperationResult> EnsureRepositoryAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await EnsureRepositoryCoreAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<GitClassification> SynchronizeAsync(string? lastPushedSha, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await SynchronizeCoreAsync(lastPushedSha, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<GitOperationResult> CommitAsync(string message, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await CommitCoreAsync(message, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<GitPushResult> PushAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var result = await RunGitAsync(["push", "origin", _gitOptions.Branch], cancellationToken, logFailureAsWarning: false);
            if (result.Success)
            {
                return GitPushResult.Ok;
            }

            var error = result.ErrorMessage ?? string.Empty;
            return IsNonFastForward(error)
                ? GitPushResult.Rejected(error)
                : GitPushResult.NetworkError(error);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<GitStatusSnapshot> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var dirty = await IsWorkingTreeDirtyAsync(cancellationToken);
            var unpushed = await CountUnpushedCommitsAsync(cancellationToken);
            return new GitStatusSnapshot(dirty, unpushed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not read git status for the health snapshot.");
            return GitStatusSnapshot.Unknown;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string?> GetHeadShaAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var result = await RunGitCapturingAsync(["rev-parse", "HEAD"], cancellationToken, logFailureAsWarning: false);
            return result.Success ? result.StandardOutput.Trim() : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<GitOperationResult> VerifyRemoteWritableAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await RunGitAsync(["push", "--dry-run", "origin", _gitOptions.Branch], cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    // ---------------------------------------------------------------------------------------
    // Core implementations. All of these assume the lock is already held.
    // ---------------------------------------------------------------------------------------

    private async Task<GitClassification> SynchronizeCoreAsync(string? lastPushedSha, CancellationToken cancellationToken)
    {
        // Step 1 — the working tree must be clean before anything else runs. A crash between
        // writing a note and committing it leaves the note uncommitted; committing it here is
        // what makes "kill -9 mid-write loses nothing" true. Never reset here: the content is ours.
        var wasDirty = await IsWorkingTreeDirtyAsync(cancellationToken);
        if (wasDirty)
        {
            var recovery = await CommitCoreAsync("Recover uncommitted notes after unclean shutdown", cancellationToken);
            if (!recovery.Success)
            {
                _logger.LogError("Working tree was dirty and could not be committed. {Error}", recovery.ErrorMessage);
                return new GitClassification(
                    GitSyncStrategy.RemoteUnreachable,
                    await CountUnpushedCommitsAsync(cancellationToken),
                    WorkingTreeWasDirty: true,
                    Error: recovery.ErrorMessage);
            }
        }

        // Step 2 — fetch. Case 4: never drop a capture because the remote is down.
        var fetchResult = await FetchBranchAsync(cancellationToken);

        if (!fetchResult.Success)
        {
            return new GitClassification(
                GitSyncStrategy.RemoteUnreachable,
                await CountUnpushedCommitsAsync(cancellationToken),
                wasDirty,
                Error: fetchResult.ErrorMessage);
        }

        // Step 3 — classify.
        var unpushed = await CountUnpushedCommitsAsync(cancellationToken);

        if (unpushed == 0)
        {
            // Case 1 — the healthy steady state. --ff-only, never a merge.
            var pull = await RunGitAsync(["pull", "--ff-only", "origin", _gitOptions.Branch], cancellationToken, logFailureAsWarning: false);
            return new GitClassification(
                GitSyncStrategy.FastForward,
                0,
                wasDirty,
                Error: pull.Success ? null : pull.ErrorMessage);
        }

        // A null lastPushedSha means this bot has never recorded a push, so the un-pushed commits
        // were never on the remote and cannot be ones the operator triaged. Replaying them is safe.
        var operatorAdvanced = lastPushedSha is null || await IsAncestorOfOriginAsync(lastPushedSha, cancellationToken);

        if (operatorAdvanced)
        {
            // Case 2 — the operator moved the branch forward without discarding our history.
            var rebase = await RunGitAsync(
                ["pull", "--rebase", "--autostash", "origin", _gitOptions.Branch],
                cancellationToken,
                logFailureAsWarning: false);

            return new GitClassification(
                GitSyncStrategy.OperatorAdvanced,
                unpushed,
                wasDirty,
                Error: rebase.Success ? null : rebase.ErrorMessage);
        }

        // Case 3 — the branch was reset or rewritten after triage. Rebasing would replay commits
        // whose notes the operator has already processed, resurrecting deleted notes. Export
        // first; only discard once the export is on disk and verified.
        return await RecoverFromRewrittenBranchAsync(unpushed, wasDirty, cancellationToken);
    }

    private async Task<GitClassification> RecoverFromRewrittenBranchAsync(
        int unpushed,
        bool wasDirty,
        CancellationToken cancellationToken)
    {
        string bundlePath;
        try
        {
            Directory.CreateDirectory(_gitOptions.RecoveryPath);
            var stamp = _timeProvider.GetUtcNow().ToString("yyyyMMdd'T'HHmmss'Z'");
            bundlePath = Path.Combine(_gitOptions.RecoveryPath, $"{SanitiseForFilename(_gitOptions.Branch)}-{stamp}.bundle");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not prepare the recovery directory '{RecoveryPath}'.", _gitOptions.RecoveryPath);
            return new GitClassification(GitSyncStrategy.RemoteRewritten, unpushed, wasDirty, Error: ex.Message);
        }

        var bundle = await RunGitAsync(
            ["bundle", "create", bundlePath, "HEAD", "--not", $"origin/{_gitOptions.Branch}"],
            cancellationToken,
            logFailureAsWarning: false);

        if (!bundle.Success)
        {
            // Never discard commits without a recovery bundle. Staying degraded is correct here.
            return new GitClassification(GitSyncStrategy.RemoteRewritten, unpushed, wasDirty, Error: bundle.ErrorMessage);
        }

        var verify = await RunGitAsync(["bundle", "verify", bundlePath], cancellationToken, logFailureAsWarning: false);
        if (!verify.Success)
        {
            return new GitClassification(GitSyncStrategy.RemoteRewritten, unpushed, wasDirty, Error: verify.ErrorMessage);
        }

        var reset = await RunGitAsync(["reset", "--hard", $"origin/{_gitOptions.Branch}"], cancellationToken, logFailureAsWarning: false);
        if (!reset.Success)
        {
            return new GitClassification(GitSyncStrategy.RemoteRewritten, unpushed, wasDirty, bundlePath, unpushed, reset.ErrorMessage);
        }

        return new GitClassification(GitSyncStrategy.RemoteRewritten, unpushed, wasDirty, bundlePath, unpushed);
    }

    private async Task<GitOperationResult> CommitCoreAsync(string message, CancellationToken cancellationToken)
    {
        var addResult = await RunGitAsync(["add", "-A"], cancellationToken);
        if (!addResult.Success)
        {
            return addResult;
        }

        var commitResult = await RunGitAsync(["commit", "-m", message], cancellationToken, logFailureAsWarning: false);
        if (!commitResult.Success && IsNothingToCommit(commitResult.ErrorMessage))
        {
            return GitOperationResult.Ok;
        }

        if (!commitResult.Success)
        {
            _logger.LogWarning("git commit failed: {Error}", commitResult.ErrorMessage);
        }

        return commitResult;
    }

    private async Task<bool> IsWorkingTreeDirtyAsync(CancellationToken cancellationToken)
    {
        var result = await RunGitCapturingAsync(["status", "--porcelain"], cancellationToken, logFailureAsWarning: false);
        return result.Success && !string.IsNullOrWhiteSpace(result.StandardOutput);
    }

    /// <summary>
    /// Commits reachable from HEAD but not from origin/&lt;branch&gt;. Zero is the invariant the
    /// bot normally holds; anything else is the degraded state the health endpoint reports.
    /// </summary>
    private async Task<int> CountUnpushedCommitsAsync(CancellationToken cancellationToken)
    {
        var result = await RunGitCapturingAsync(
            ["rev-list", "--count", $"origin/{_gitOptions.Branch}..HEAD"],
            cancellationToken,
            logFailureAsWarning: false);

        if (!result.Success)
        {
            // No remote-tracking ref yet (fresh branch): report nothing un-pushed rather than
            // guessing, since the caller only uses this to choose between safe strategies.
            return 0;
        }

        return int.TryParse(result.StandardOutput.Trim(), out var count) ? count : 0;
    }

    /// <summary>
    /// Fetches the bot's branch with an explicit, forced refspec.
    /// <para>
    /// Both halves matter. The repository is cloned with <c>--single-branch</c>, which pins
    /// <c>remote.origin.fetch</c> to the default branch only — so a plain <c>git fetch origin
    /// &lt;branch&gt;</c> lands in FETCH_HEAD and never creates <c>refs/remotes/origin/&lt;branch&gt;</c>.
    /// Without that ref every <c>rev-list</c>, <c>merge-base</c> and <c>reset</c> below silently
    /// fails and the whole classification collapses to "nothing un-pushed". The leading '+' is
    /// what lets the ref move when the operator rewrites the branch; a non-forced update would be
    /// rejected as non-fast-forward, which is precisely the case this exists to detect.
    /// </para>
    /// </summary>
    private Task<GitOperationResult> FetchBranchAsync(CancellationToken cancellationToken) =>
        RunGitAsync(
            ["fetch", "origin", $"+refs/heads/{_gitOptions.Branch}:refs/remotes/origin/{_gitOptions.Branch}"],
            cancellationToken,
            logFailureAsWarning: false);

    /// <summary>
    /// Points remote.origin.fetch at the bot's branch, so the tracking ref stays correct for
    /// operator debugging inside the container too. Idempotent: --replace-all overwrites whatever
    /// the clone left behind rather than accumulating refspecs across restarts.
    /// </summary>
    private Task<GitOperationResult> ConfigureFetchRefspecAsync(CancellationToken cancellationToken) =>
        RunGitAsync(
            [
                "config", "--local", "--replace-all", "remote.origin.fetch",
                $"+refs/heads/{_gitOptions.Branch}:refs/remotes/origin/{_gitOptions.Branch}",
            ],
            cancellationToken);

    private async Task<bool> IsAncestorOfOriginAsync(string sha, CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(
            ["merge-base", "--is-ancestor", sha, $"origin/{_gitOptions.Branch}"],
            cancellationToken,
            logFailureAsWarning: false);

        return result.Success;
    }

    private async Task<GitOperationResult> EnsureRepositoryCoreAsync(CancellationToken cancellationToken)
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

        var refspecResult = await ConfigureFetchRefspecAsync(cancellationToken);
        if (!refspecResult.Success)
        {
            return refspecResult;
        }

        var localBranchResult = await LocalBranchExistsAsync(cancellationToken);
        if (localBranchResult.Success)
        {
            var checkoutExistingResult = await RunGitAsync(["checkout", _gitOptions.Branch], cancellationToken);
            if (!checkoutExistingResult.Success)
            {
                return checkoutExistingResult;
            }

            // Best-effort: establishes refs/remotes/origin/<branch> if a previous run never did.
            // A failure here just means the remote is down, which SynchronizeAsync handles.
            await FetchBranchAsync(cancellationToken);

            _logger.LogInformation(
                "Branch '{Branch}' already exists locally; checked it out without resetting to origin so any un-pushed commits are preserved.",
                _gitOptions.Branch);
            return GitOperationResult.Ok;
        }

        var fetchResult = await FetchBranchAsync(cancellationToken);

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

        // 'push -u' sets the upstream config but does NOT create refs/remotes/origin/<branch>
        // under a --single-branch clone's refspec. Fetch explicitly so the classification has the
        // tracking ref it depends on from the very first run.
        var trackingResult = await FetchBranchAsync(cancellationToken);
        if (!trackingResult.Success)
        {
            return trackingResult;
        }

        _logger.LogInformation(
            "Branch '{Branch}' did not exist on origin; created it from the default branch and pushed it.",
            _gitOptions.Branch);
        return GitOperationResult.Ok;
    }

    private async Task<GitOperationResult> ConfigureLocalIdentityAsync(CancellationToken cancellationToken)
    {
        var nameResult = await RunGitAsync(["config", "--local", "user.name", _gitOptions.UserName], cancellationToken);
        if (!nameResult.Success)
        {
            return nameResult;
        }

        return await RunGitAsync(["config", "--local", "user.email", _gitOptions.UserEmail], cancellationToken);
    }

    /// <summary>
    /// Checks whether the configured branch already exists as a local ref. Used to avoid
    /// resetting an existing local branch to origin's history, which would discard commits
    /// made locally but not yet pushed (e.g. after a prior push failure).
    /// </summary>
    private Task<GitOperationResult> LocalBranchExistsAsync(CancellationToken cancellationToken) =>
        RunGitAsync(["rev-parse", "--verify", "--quiet", $"refs/heads/{_gitOptions.Branch}"], cancellationToken, logFailureAsWarning: false);

    private static bool IsNothingToCommit(string? output) =>
        output?.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase) == true ||
        output?.Contains("nothing added to commit", StringComparison.OrdinalIgnoreCase) == true ||
        output?.Contains("working tree clean", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Distinguishes "the remote moved under us" from "we could not reach the remote". Only the
    /// former warrants re-classification; the latter just needs a backoff.
    /// </summary>
    private static bool IsNonFastForward(string output) =>
        output.Contains("non-fast-forward", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("fetch first", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("rejected", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("stale info", StringComparison.OrdinalIgnoreCase);

    private static string SanitiseForFilename(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(c => invalid.Contains(c) ? '-' : c));
    }

    private Dictionary<string, string?> BuildEnvironment()
    {
        var sshCommand = $"ssh -i {_gitOptions.SshKeyPath} -o StrictHostKeyChecking=yes";
        if (!string.IsNullOrWhiteSpace(_gitOptions.KnownHostsPath))
        {
            sshCommand += $" -o UserKnownHostsFile={_gitOptions.KnownHostsPath}";
        }

        return new Dictionary<string, string?>
        {
            ["GIT_SSH_COMMAND"] = sshCommand,
            // Nothing in this process is interactive; a credential prompt would hang the worker.
            ["GIT_TERMINAL_PROMPT"] = "0",
        };
    }

    private Task<GitOperationResult> RunGitAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool logFailureAsWarning = true) =>
        RunGitInAsync(_vaultOptions.Root, arguments, cancellationToken, logFailureAsWarning);

    private async Task<GitOperationResult> RunGitInAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool logFailureAsWarning = true)
    {
        var result = await RunProcessAsync(workingDirectory, arguments, cancellationToken, logFailureAsWarning);
        return result.Success ? GitOperationResult.Ok : GitOperationResult.Fail(result.CombinedOutput);
    }

    private async Task<(bool Success, string StandardOutput)> RunGitCapturingAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool logFailureAsWarning = true)
    {
        var result = await RunProcessAsync(_vaultOptions.Root, arguments, cancellationToken, logFailureAsWarning);
        return (result.Success, result.StandardOutput);
    }

    private async Task<ProcessResult> RunProcessAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool logFailureAsWarning)
    {
        var result = await ProcessRunner.RunAsync("git", arguments, workingDirectory, BuildEnvironment(), cancellationToken);

        if (!result.Success && logFailureAsWarning)
        {
            _logger.LogWarning(
                "git {Arguments} failed with exit code {ExitCode}: {Output}",
                string.Join(' ', arguments),
                result.ExitCode,
                result.CombinedOutput);
        }

        return result;
    }
}
