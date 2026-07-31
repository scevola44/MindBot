using MindBot.Core.Git;
using MindBot.Tests.Fakes;

namespace MindBot.Tests;

/// <summary>
/// The four pre-write classification cases, against a real local bare repository used as a
/// file:// remote. Cases 2–4 only ever run when the bot is already degraded, which makes them the
/// rarely-exercised code — hence the coverage here rather than confidence.
/// </summary>
public sealed class GitClassificationTests : IDisposable
{
    private readonly TestGitRepository _repo = new();

    [Fact]
    public async Task EnsureRepositoryAsync_EstablishesTheRemoteTrackingRef()
    {
        // Regression guard. The clone is --single-branch, which pins remote.origin.fetch to the
        // default branch, and 'push -u' does not create refs/remotes/origin/<branch> under that
        // refspec. Without this ref every rev-list, merge-base and reset below fails silently and
        // the classification degrades to "nothing is ever un-pushed" — the failure mode is quiet,
        // so it is asserted directly.
        var git = _repo.CreateGitService();

        await git.EnsureRepositoryAsync();

        var trackingRef = TestGitRepository.Git(
            _repo.VaultRoot, "rev-parse", "--verify", "--quiet", "refs/remotes/origin/bot-inbox").Trim();

        Assert.False(string.IsNullOrEmpty(trackingRef));

        _repo.CommitInVault("note.md", "note\n", "Add note note.md");
        Assert.Equal(1, (await git.GetStatusAsync()).UnpushedCommitCount);
    }

    [Fact]
    public async Task Case1_NoUnpushedCommits_IsFastForward()
    {
        var git = _repo.CreateGitService();
        await git.EnsureRepositoryAsync();

        var classification = await git.SynchronizeAsync(lastPushedSha: null);

        Assert.Equal(GitSyncStrategy.FastForward, classification.Strategy);
        Assert.Equal(0, classification.UnpushedCommitCount);
        Assert.False(classification.WorkingTreeWasDirty);
        Assert.Null(classification.Error);
    }

    [Fact]
    public async Task Case1_OperatorPushedWhileBotHadNothingLocal_FastForwardsOntoIt()
    {
        var git = _repo.CreateGitService();
        await git.EnsureRepositoryAsync();
        await git.PushAsync();

        _repo.OperatorPush("operator-note.md", "from the operator\n", "operator commit");

        var classification = await git.SynchronizeAsync(lastPushedSha: null);

        Assert.Equal(GitSyncStrategy.FastForward, classification.Strategy);
        Assert.Null(classification.Error);
        Assert.True(File.Exists(Path.Combine(_repo.VaultRoot, "operator-note.md")));
    }

    [Fact]
    public async Task Case2_OperatorAdvancedBranch_RebasesUnpushedCommits()
    {
        var git = _repo.CreateGitService();
        await git.EnsureRepositoryAsync();
        await git.PushAsync();

        // The SHA the bot last pushed, and which the operator then built on top of.
        var lastPushedSha = await git.GetHeadShaAsync();
        Assert.NotNull(lastPushedSha);

        _repo.OperatorPush("operator-note.md", "from the operator\n", "operator commit");
        _repo.CommitInVault("bot-note.md", "from the bot\n", "Add note bot-note.md");

        var classification = await git.SynchronizeAsync(lastPushedSha);

        Assert.Equal(GitSyncStrategy.OperatorAdvanced, classification.Strategy);
        Assert.Equal(1, classification.UnpushedCommitCount);
        Assert.Null(classification.Error);

        // Both the operator's commit and the bot's un-pushed commit survive the rebase.
        var log = _repo.VaultLog();
        Assert.Contains("operator commit", log);
        Assert.Contains("Add note bot-note.md", log);
        Assert.True(File.Exists(Path.Combine(_repo.VaultRoot, "operator-note.md")));
        Assert.True(File.Exists(Path.Combine(_repo.VaultRoot, "bot-note.md")));
    }

    [Fact]
    public async Task Case3_RemoteResetToUnrelatedCommit_BundlesBothCommitsAndDoesNotRebase()
    {
        var git = _repo.CreateGitService();
        await git.EnsureRepositoryAsync();
        await git.PushAsync();

        var lastPushedSha = await git.GetHeadShaAsync();
        Assert.NotNull(lastPushedSha);

        // The bot holds two un-pushed commits...
        _repo.CommitInVault("first.md", "first note\n", "Add note first.md");
        _repo.CommitInVault("second.md", "second note\n", "Add note second.md");

        // ...and the operator resets the branch to something unrelated after triaging them.
        _repo.ResetRemoteToUnrelatedCommit();

        var classification = await git.SynchronizeAsync(lastPushedSha);

        Assert.Equal(GitSyncStrategy.RemoteRewritten, classification.Strategy);
        Assert.Null(classification.Error);

        // Once the remote shares no ancestry with the local branch, *everything* local is
        // un-pushed — the two notes plus the seed commit they were built on. Capturing more than
        // the two is the safe direction: nothing is discarded that is not in the bundle.
        Assert.True(
            classification.UnpushedCommitCount >= 2,
            $"expected at least the two note commits, got {classification.UnpushedCommitCount}");
        Assert.Equal(classification.UnpushedCommitCount, classification.RecoveredCommitCount);

        // The bundle exists and contains both commits.
        Assert.NotNull(classification.RecoveryBundlePath);
        Assert.True(File.Exists(classification.RecoveryBundlePath));

        var subjects = _repo.BundleCommitSubjects(classification.RecoveryBundlePath!);
        Assert.Contains("Add note first.md", subjects);
        Assert.Contains("Add note second.md", subjects);

        // Nothing was rebased onto the rewritten branch: HEAD is exactly origin, and neither
        // already-triaged note has been resurrected in the working tree.
        Assert.Equal(_repo.RemoteBranchSha(), _repo.HeadSha(_repo.VaultRoot));
        Assert.False(File.Exists(Path.Combine(_repo.VaultRoot, "first.md")));
        Assert.False(File.Exists(Path.Combine(_repo.VaultRoot, "second.md")));

        var log = _repo.VaultLog();
        Assert.DoesNotContain("Add note first.md", log);
        Assert.DoesNotContain("Add note second.md", log);
    }

    [Fact]
    public async Task Case3_RecoveryBundleIsWrittenOutsideTheVault()
    {
        var git = _repo.CreateGitService();
        await git.EnsureRepositoryAsync();
        await git.PushAsync();
        var lastPushedSha = await git.GetHeadShaAsync();

        _repo.CommitInVault("note.md", "note\n", "Add note note.md");
        _repo.ResetRemoteToUnrelatedCommit();

        var classification = await git.SynchronizeAsync(lastPushedSha);

        // A bundle inside the vault would be swept into the next commit by 'git add -A'.
        Assert.NotNull(classification.RecoveryBundlePath);
        var bundleFull = Path.GetFullPath(classification.RecoveryBundlePath!);
        var vaultFull = Path.GetFullPath(_repo.VaultRoot) + Path.DirectorySeparatorChar;
        Assert.False(bundleFull.StartsWith(vaultFull, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Case4_RemoteUnreachable_ProceedsWithLocalWrite()
    {
        var git = _repo.CreateGitService();
        await git.EnsureRepositoryAsync();

        // Point the clone at a remote that does not exist, so fetch fails for "network" reasons.
        TestGitRepository.Git(_repo.VaultRoot, "remote", "set-url", "origin", Path.Combine(_repo.Root, "gone.git"));

        var classification = await git.SynchronizeAsync(lastPushedSha: null);

        Assert.Equal(GitSyncStrategy.RemoteUnreachable, classification.Strategy);
        Assert.NotNull(classification.Error);

        // The capture still lands locally — a down remote must never drop a note.
        File.WriteAllText(Path.Combine(_repo.VaultRoot, "offline.md"), "captured offline\n");
        var commit = await git.CommitAsync("Add note offline.md");

        Assert.True(commit.Success, commit.ErrorMessage);
        Assert.Contains("Add note offline.md", _repo.VaultLog());
    }

    [Fact]
    public async Task DirtyWorkingTree_IsCommittedBeforeClassifying_SoNothingIsLost()
    {
        var git = _repo.CreateGitService();
        await git.EnsureRepositoryAsync();
        await git.PushAsync();

        // Exactly what a kill -9 between writing the file and committing it leaves behind.
        File.WriteAllText(Path.Combine(_repo.VaultRoot, "half-written.md"), "captured but never committed\n");

        var classification = await git.SynchronizeAsync(lastPushedSha: null);

        Assert.True(classification.WorkingTreeWasDirty);
        Assert.Contains("Recover uncommitted notes after unclean shutdown", _repo.VaultLog());
        Assert.True(File.Exists(Path.Combine(_repo.VaultRoot, "half-written.md")));
    }

    [Fact]
    public async Task GetStatusAsync_ReportsUnpushedCommitsAndDirtyTree()
    {
        var git = _repo.CreateGitService();
        await git.EnsureRepositoryAsync();
        await git.PushAsync();

        Assert.Equal(new GitStatusSnapshot(false, 0), await git.GetStatusAsync());

        _repo.CommitInVault("pending.md", "pending\n", "Add note pending.md");
        File.WriteAllText(Path.Combine(_repo.VaultRoot, "uncommitted.md"), "uncommitted\n");

        var status = await git.GetStatusAsync();

        Assert.True(status.WorkingTreeDirty);
        Assert.Equal(1, status.UnpushedCommitCount);
    }

    public void Dispose() => _repo.Dispose();
}
