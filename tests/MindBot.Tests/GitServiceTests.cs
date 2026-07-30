using System.Diagnostics;
using MindBot.Core.Options;
using MindBot.Infrastructure.Git;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MindBot.Tests;

/// <summary>
/// Exercises GitService against a real local bare repository standing in for the remote,
/// covering the EnsureRepository/Pull/Commit/Push flows described in the project scope.
/// </summary>
public sealed class GitServiceTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _barePath;
    private readonly string _vaultRoot;
    private readonly string _dummySshKey;

    public GitServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "mindbot-git-tests-" + Guid.NewGuid());
        _barePath = Path.Combine(_testRoot, "remote.git");
        _vaultRoot = Path.Combine(_testRoot, "vault");
        _dummySshKey = Path.Combine(_testRoot, "dummy-key");

        Directory.CreateDirectory(_testRoot);
        File.WriteAllText(_dummySshKey, "not-a-real-key");

        RunGit(_testRoot, "init", "--bare", "--initial-branch=main", _barePath);
        SeedInitialCommit();
    }

    private void SeedInitialCommit()
    {
        var seedPath = Path.Combine(_testRoot, "seed");
        RunGit(_testRoot, "clone", _barePath, seedPath);
        File.WriteAllText(Path.Combine(seedPath, "README.md"), "# Vault\n");
        RunGit(seedPath, "-c", "user.name=Seed", "-c", "user.email=seed@example.com", "add", "-A");
        RunGit(seedPath, "-c", "user.name=Seed", "-c", "user.email=seed@example.com", "commit", "-m", "initial commit");
        RunGit(seedPath, "push", "origin", "main");
    }

    private GitService CreateGitService(string branch) =>
        new(
            Options.Create(new GitOptions
            {
                RemoteUrl = _barePath,
                Branch = branch,
                SshKeyPath = _dummySshKey,
                UserName = "MindBot",
                UserEmail = "mindbot@example.com",
            }),
            Options.Create(new VaultOptions { Root = _vaultRoot }),
            NullLogger<GitService>.Instance);

    [Fact]
    public async Task EnsureRepositoryAsync_BranchMissingOnRemote_CreatesAndPushesIt()
    {
        var git = CreateGitService("bot-inbox");

        var result = await git.EnsureRepositoryAsync();

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(Directory.Exists(Path.Combine(_vaultRoot, ".git")));

        var remoteRefs = RunGit(_testRoot, "ls-remote", "--heads", _barePath).StandardOutput;
        Assert.Contains("refs/heads/bot-inbox", remoteRefs);
    }

    [Fact]
    public async Task EnsureRepositoryAsync_BranchExistsOnRemote_ChecksItOut()
    {
        var firstRun = CreateGitService("bot-inbox");
        await firstRun.EnsureRepositoryAsync();
        Directory.Delete(_vaultRoot, recursive: true);

        var secondRun = CreateGitService("bot-inbox");
        var result = await secondRun.EnsureRepositoryAsync();

        Assert.True(result.Success, result.ErrorMessage);
        var branch = RunGit(_vaultRoot, "rev-parse", "--abbrev-ref", "HEAD").StandardOutput.Trim();
        Assert.Equal("bot-inbox", branch);
    }

    [Fact]
    public async Task EnsureRepositoryAsync_CalledAgainWithUnpushedLocalCommit_PreservesTheCommit()
    {
        var git = CreateGitService("bot-inbox");
        await git.EnsureRepositoryAsync();

        // Simulate a note committed locally while the remote was unreachable, so it was
        // never pushed.
        File.WriteAllText(Path.Combine(_vaultRoot, "unpushed-note.md"), "---\ncreated: now\n---\n\nHello\n");
        var commitResult = await git.CommitAsync("Add note unpushed-note.md");
        Assert.True(commitResult.Success, commitResult.ErrorMessage);

        // Simulate a process restart: EnsureRepositoryAsync runs again against the same
        // on-disk clone, with the commit above still un-pushed. It must not be discarded.
        var result = await git.EnsureRepositoryAsync();

        Assert.True(result.Success, result.ErrorMessage);
        var log = RunGit(_vaultRoot, "log", "--oneline").StandardOutput;
        Assert.Contains("Add note unpushed-note.md", log);
    }

    [Fact]
    public async Task FullPipeline_PullWriteCommitPush_LandsCommitOnRemote()
    {
        var git = CreateGitService("bot-inbox");
        await git.EnsureRepositoryAsync();

        var pullResult = await git.PullAsync();
        Assert.True(pullResult.Success, pullResult.ErrorMessage);

        File.WriteAllText(Path.Combine(_vaultRoot, "2026-07-30T090000-note.md"), "---\ncreated: now\n---\n\nHello\n");

        var commitResult = await git.CommitAsync("Add note 2026-07-30T090000-note.md");
        Assert.True(commitResult.Success, commitResult.ErrorMessage);

        var pushResult = await git.PushAsync();
        Assert.True(pushResult.Success, pushResult.ErrorMessage);

        var log = RunGit(_testRoot, "--git-dir", _barePath, "log", "bot-inbox", "--oneline").StandardOutput;
        Assert.Contains("Add note", log);
    }

    [Fact]
    public async Task VerifyRemoteWritableAsync_AfterEnsureRepository_Succeeds()
    {
        var git = CreateGitService("bot-inbox");
        await git.EnsureRepositoryAsync();

        var result = await git.VerifyRemoteWritableAsync();

        Assert.True(result.Success, result.ErrorMessage);
    }

    [Fact]
    public async Task CommitAsync_NoChanges_IsIdempotentSuccess()
    {
        var git = CreateGitService("bot-inbox");
        await git.EnsureRepositoryAsync();

        var result = await git.CommitAsync("nothing to see here");

        Assert.True(result.Success, result.ErrorMessage);
    }

    private static (int ExitCode, string StandardOutput, string StandardError) RunGit(string workingDirectory, params string[] args)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
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
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stderr}");
        }

        return (process.ExitCode, stdout, stderr);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup of a temp directory
        }
    }
}
