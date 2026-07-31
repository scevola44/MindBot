using MindBot.Tests.Fakes;

namespace MindBot.Tests;

/// <summary>
/// Exercises GitService against a real local bare repository standing in for the remote, covering
/// the repository-preparation and commit/push flows. The four-way pre-write classification has its
/// own suite in <see cref="GitClassificationTests"/>.
/// </summary>
public sealed class GitServiceTests : IDisposable
{
    private readonly TestGitRepository _repo = new();

    [Fact]
    public async Task EnsureRepositoryAsync_BranchMissingOnRemote_CreatesAndPushesIt()
    {
        var git = _repo.CreateGitService();

        var result = await git.EnsureRepositoryAsync();

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(Directory.Exists(Path.Combine(_repo.VaultRoot, ".git")));

        var remoteRefs = TestGitRepository.Git(_repo.Root, "ls-remote", "--heads", _repo.BarePath);
        Assert.Contains("refs/heads/bot-inbox", remoteRefs);
    }

    [Fact]
    public async Task EnsureRepositoryAsync_BranchExistsOnRemote_ChecksItOut()
    {
        var firstRun = _repo.CreateGitService();
        await firstRun.EnsureRepositoryAsync();
        Directory.Delete(_repo.VaultRoot, recursive: true);

        var secondRun = _repo.CreateGitService();
        var result = await secondRun.EnsureRepositoryAsync();

        Assert.True(result.Success, result.ErrorMessage);
        var branch = TestGitRepository.Git(_repo.VaultRoot, "rev-parse", "--abbrev-ref", "HEAD").Trim();
        Assert.Equal("bot-inbox", branch);
    }

    [Fact]
    public async Task EnsureRepositoryAsync_CalledAgainWithUnpushedLocalCommit_PreservesTheCommit()
    {
        var git = _repo.CreateGitService();
        await git.EnsureRepositoryAsync();

        // Simulate a note committed locally while the remote was unreachable, so it was never pushed.
        File.WriteAllText(Path.Combine(_repo.VaultRoot, "unpushed-note.md"), "---\ndate: now\n---\n\nHello\n");
        var commitResult = await git.CommitAsync("Add note unpushed-note.md");
        Assert.True(commitResult.Success, commitResult.ErrorMessage);

        // Simulate a process restart: EnsureRepositoryAsync runs again against the same on-disk
        // clone, with the commit above still un-pushed. It must not be discarded.
        var result = await git.EnsureRepositoryAsync();

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Contains("Add note unpushed-note.md", _repo.VaultLog());
    }

    [Fact]
    public async Task FullPipeline_SynchronizeWriteCommitPush_LandsCommitOnRemote()
    {
        var git = _repo.CreateGitService();
        await git.EnsureRepositoryAsync();

        var classification = await git.SynchronizeAsync(lastPushedSha: null);
        Assert.Null(classification.Error);

        File.WriteAllText(Path.Combine(_repo.VaultRoot, "202607300900.md"), "---\ndate: now\n---\n\nHello\n");

        var commitResult = await git.CommitAsync("Add note 202607300900.md");
        Assert.True(commitResult.Success, commitResult.ErrorMessage);

        var pushResult = await git.PushAsync();
        Assert.True(pushResult.Success, pushResult.ErrorMessage);

        Assert.Contains("Add note", _repo.RemoteLog());
    }

    [Fact]
    public async Task PushAsync_NonFastForwardRejection_IsReportedAsRejectedNotNetwork()
    {
        var git = _repo.CreateGitService();
        await git.EnsureRepositoryAsync();
        await git.PushAsync();

        // The operator pushes, then the bot commits on top of its now-stale view of the branch.
        _repo.OperatorPush("operator-note.md", "from the operator\n", "operator commit");
        _repo.CommitInVault("bot-note.md", "from the bot\n", "Add note bot-note.md");

        var result = await git.PushAsync();

        Assert.False(result.Success);
        Assert.Equal(MindBot.Core.Git.GitPushFailure.Rejected, result.Failure);
    }

    [Fact]
    public async Task PushAsync_UnreachableRemote_IsReportedAsNetworkFailure()
    {
        var git = _repo.CreateGitService();
        await git.EnsureRepositoryAsync();

        TestGitRepository.Git(_repo.VaultRoot, "remote", "set-url", "origin", Path.Combine(_repo.Root, "gone.git"));
        _repo.CommitInVault("bot-note.md", "from the bot\n", "Add note bot-note.md");

        var result = await git.PushAsync();

        Assert.False(result.Success);
        Assert.Equal(MindBot.Core.Git.GitPushFailure.Network, result.Failure);
    }

    [Fact]
    public async Task VerifyRemoteWritableAsync_AfterEnsureRepository_Succeeds()
    {
        var git = _repo.CreateGitService();
        await git.EnsureRepositoryAsync();

        var result = await git.VerifyRemoteWritableAsync();

        Assert.True(result.Success, result.ErrorMessage);
    }

    [Fact]
    public async Task CommitAsync_NoChanges_IsIdempotentSuccess()
    {
        var git = _repo.CreateGitService();
        await git.EnsureRepositoryAsync();

        var result = await git.CommitAsync("nothing to see here");

        Assert.True(result.Success, result.ErrorMessage);
    }

    [Fact]
    public async Task GetHeadShaAsync_ReturnsTheCurrentCommit()
    {
        var git = _repo.CreateGitService();
        await git.EnsureRepositoryAsync();

        var sha = await git.GetHeadShaAsync();

        Assert.Equal(_repo.HeadSha(_repo.VaultRoot), sha);
    }

    public void Dispose() => _repo.Dispose();
}
