using System.Diagnostics;
using MindBot.Core.Options;
using MindBot.Infrastructure.Git;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MindBot.Tests.Fakes;

/// <summary>
/// A local bare repository standing in for the remote, plus a vault clone and a recovery
/// directory. Everything runs over file:// paths, so the whole git path is exercised with no
/// network and no SSH.
/// </summary>
public sealed class TestGitRepository : IDisposable
{
    public TestGitRepository(string branch = "bot-inbox")
    {
        Branch = branch;
        Root = Path.Combine(Path.GetTempPath(), "mindbot-git-tests-" + Guid.NewGuid());
        BarePath = Path.Combine(Root, "remote.git");
        VaultRoot = Path.Combine(Root, "vault");
        RecoveryPath = Path.Combine(Root, "recovery");
        SshKeyPath = Path.Combine(Root, "dummy-key");

        Directory.CreateDirectory(Root);
        File.WriteAllText(SshKeyPath, "not-a-real-key");

        Git(Root, "init", "--bare", "--initial-branch=main", BarePath);
        SeedInitialCommit();
    }

    public string Branch { get; }

    public string Root { get; }

    public string BarePath { get; }

    public string VaultRoot { get; }

    public string RecoveryPath { get; }

    public string SshKeyPath { get; }

    public GitOptions Options { get; private set; } = null!;

    public GitService CreateGitService(TimeProvider? timeProvider = null, string? remoteUrlOverride = null)
    {
        Options = new GitOptions
        {
            RemoteUrl = remoteUrlOverride ?? BarePath,
            Branch = Branch,
            SshKeyPath = SshKeyPath,
            RecoveryPath = RecoveryPath,
            UserName = "MindBot",
            UserEmail = "mindbot@example.com",
        };

        return new GitService(
            Microsoft.Extensions.Options.Options.Create(Options),
            Microsoft.Extensions.Options.Options.Create(new VaultOptions { Root = VaultRoot }),
            timeProvider ?? TimeProvider.System,
            NullLogger<GitService>.Instance);
    }

    /// <summary>Commits a file in the bot's vault clone without going through GitService.</summary>
    public void CommitInVault(string filename, string content, string message)
    {
        var path = Path.Combine(VaultRoot, filename);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        Git(VaultRoot, "add", "-A");
        Git(VaultRoot, "commit", "-m", message);
    }

    /// <summary>
    /// Clones the remote separately, commits there and pushes — i.e. what the operator does by
    /// hand, entirely outside the bot's working tree.
    /// </summary>
    public void OperatorPush(string filename, string content, string message)
    {
        var workspace = Path.Combine(Root, "operator-" + Guid.NewGuid().ToString("N")[..8]);
        Git(Root, "clone", "--branch", Branch, BarePath, workspace);
        File.WriteAllText(Path.Combine(workspace, filename), content);
        Git(workspace, "-c", "user.name=Operator", "-c", "user.email=op@example.com", "add", "-A");
        Git(workspace, "-c", "user.name=Operator", "-c", "user.email=op@example.com", "commit", "-m", message);
        Git(workspace, "push", "origin", Branch);
    }

    /// <summary>
    /// Force-resets the remote branch to a commit unrelated to anything the bot has pushed — the
    /// "operator rewrote history after triage" case.
    /// </summary>
    public void ResetRemoteToUnrelatedCommit(string message = "unrelated history")
    {
        var workspace = Path.Combine(Root, "rewrite-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(workspace);
        Git(workspace, "init", "--initial-branch=" + Branch);
        Git(workspace, "config", "user.name", "Operator");
        Git(workspace, "config", "user.email", "op@example.com");
        File.WriteAllText(Path.Combine(workspace, "unrelated.md"), "totally different history\n");
        Git(workspace, "add", "-A");
        Git(workspace, "commit", "-m", message);
        Git(workspace, "remote", "add", "origin", BarePath);
        Git(workspace, "push", "--force", "origin", Branch);
    }

    public string HeadSha(string workingDirectory) => Git(workingDirectory, "rev-parse", "HEAD").Trim();

    public string RemoteBranchSha() => Git(Root, "--git-dir", BarePath, "rev-parse", Branch).Trim();

    public string VaultLog() => Git(VaultRoot, "log", "--oneline");

    public string RemoteLog() => Git(Root, "--git-dir", BarePath, "log", Branch, "--oneline");

    /// <summary>Subjects of the commits contained in a recovery bundle.</summary>
    public IReadOnlyList<string> BundleCommitSubjects(string bundlePath)
    {
        var workspace = Path.Combine(Root, "bundle-read-" + Guid.NewGuid().ToString("N")[..8]);
        Git(Root, "clone", BarePath, workspace);
        Git(workspace, "fetch", bundlePath, "HEAD:recovered");

        var log = Git(workspace, "log", "--format=%s", "recovered", "--not", $"origin/{Branch}");
        return log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private void SeedInitialCommit()
    {
        var seedPath = Path.Combine(Root, "seed");
        Git(Root, "clone", BarePath, seedPath);
        File.WriteAllText(Path.Combine(seedPath, "README.md"), "# Vault\n");
        Git(seedPath, "-c", "user.name=Seed", "-c", "user.email=seed@example.com", "add", "-A");
        Git(seedPath, "-c", "user.name=Seed", "-c", "user.email=seed@example.com", "commit", "-m", "initial commit");
        Git(seedPath, "push", "origin", "main");
    }

    public static string Git(string workingDirectory, params string[] args)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        // Keep the developer's own git config out of the test's way.
        startInfo.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
        startInfo.Environment["GIT_CONFIG_SYSTEM"] = "/dev/null";
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stderr}{stdout}");
        }

        return stdout;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup of a temp directory
        }
    }
}
