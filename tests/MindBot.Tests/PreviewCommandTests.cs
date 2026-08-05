using MindBot.Core.Commands;
using MindBot.Core.Health;
using MindBot.Core.Operations;
using MindBot.Core.Options;
using MindBot.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MindBot.Tests;

public class PreviewCommandTests
{
    private static (CommandDispatcher Dispatcher, InMemoryIngestUnitOfWork UnitOfWork, TempVaultRoot VaultRoot, FakeGitService Git, FakeVaultWriter Writer) CreatePipeline()
    {
        var vaultRoot = new TempVaultRoot();
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.Zero));
        var git = new FakeGitService();
        var writer = new FakeVaultWriter(vaultRoot.Path);

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddSingleton(Options.Create(new VaultOptions { Root = vaultRoot.Path }));
        services.AddSingleton<VaultOperationApplier>();
        services.AddSingleton<IVaultOperationHandler, CreateNoteHandler>();
        services.AddSingleton<IVaultOperationHandler, AppendToNoteHandler>();
        services.AddSingleton(new HealthReportService(new HealthSnapshot(), new InMemoryWriteJobQueue(), timeProvider));
        services.AddSingleton<ICommand, AppendCommand>();
        services.AddSingleton<ICommand, StatusCommand>();
        services.AddSingleton<ICommand, PreviewCommand>();
        services.AddSingleton<ICommand, YouTubeSummaryCommand>();
        services.AddSingleton<ICommand, BareTextCommand>();
        services.AddSingleton<CommandDispatcher>();
        services.AddSingleton(Options.Create(new N8nOptions { BaseUrl = "https://n8n.example/webhook" }));

        var dispatcher = services.BuildServiceProvider().GetRequiredService<CommandDispatcher>();
        return (dispatcher, new InMemoryIngestUnitOfWork(vaultRoot.Path), vaultRoot, git, writer);
    }

    [Fact]
    public async Task Preview_OfBareText_ReturnsResolvedContent_WithoutEnqueueingOrTouchingGitOrDisk()
    {
        var (dispatcher, unitOfWork, vaultRoot, git, writer) = CreatePipeline();
        using var _ = vaultRoot;
        var command = dispatcher.Match("/preview Just a quick thought");
        var context = new UnitOfWorkVaultOperationContext(unitOfWork, vaultRoot.Path);

        var result = await command.HandleAsync("/preview Just a quick thought", context);

        var direct = Assert.IsType<CommandResult.DirectReply>(result);
        Assert.Contains("Just a quick thought", direct.Text);
        Assert.Empty(unitOfWork.Enqueued);
        Assert.Empty(writer.Written); // spy: the real IVaultWriter is never invoked by /preview
        Assert.Empty(git.CommitMessages); // spy: the real IGitService is never invoked by /preview
        Assert.Equal(0, git.PushCalls);
        Assert.Empty(git.SynchronizeCalls);
        Assert.Null(Directory.GetFiles(vaultRoot.Path, "*.md", SearchOption.AllDirectories).FirstOrDefault());
    }

    [Fact]
    public async Task Preview_OfAppendAgainstSeededScratchFile_LeavesRealFileByteUnchanged()
    {
        var (dispatcher, unitOfWork, vaultRoot, git, writer) = CreatePipeline();
        using var _ = vaultRoot;
        const string seeded = "---\ndate: 2026-07-01T00:00:00+00:00\nunknownKey: value\n---\n\n- existing\n";
        vaultRoot.WriteFile("", "Scratch.md", seeded);

        var command = dispatcher.Match("/preview /append new line");
        var context = new UnitOfWorkVaultOperationContext(unitOfWork, vaultRoot.Path);

        var result = await command.HandleAsync("/preview /append new line", context);

        var direct = Assert.IsType<CommandResult.DirectReply>(result);
        Assert.Contains("new line", direct.Text);
        Assert.Contains("unknownKey: value", direct.Text);
        Assert.Equal(seeded, vaultRoot.ReadFile("", "Scratch.md")); // real file untouched
        Assert.Empty(unitOfWork.Enqueued);
        Assert.Empty(writer.Written);
        Assert.Empty(git.CommitMessages);
    }

    /// <summary>
    /// The inner command only describes the job, so previewing it cannot reach n8n — there is no
    /// HTTP client in this pipeline at all, and constructing one would fail the test.
    /// </summary>
    [Fact]
    public async Task Preview_OfYouTubeSummary_DescribesTheJob_WithoutQueueingIt()
    {
        var (dispatcher, unitOfWork, vaultRoot, git, writer) = CreatePipeline();
        using var _ = vaultRoot;
        const string message = "/preview /ytsummary https://youtu.be/qIeJ7Gw9v_I 3";
        var command = dispatcher.Match(message);
        var context = new UnitOfWorkVaultOperationContext(unitOfWork, vaultRoot.Path);

        var result = await command.HandleAsync(message, context);

        var direct = Assert.IsType<CommandResult.DirectReply>(result);
        Assert.Contains("youtube-summary", direct.Text);
        Assert.Contains("qIeJ7Gw9v_I", direct.Text);
        Assert.Empty(unitOfWork.EnqueuedBackgroundJobs);
        Assert.Empty(unitOfWork.Enqueued);
        Assert.Empty(writer.Written);
        Assert.Empty(git.CommitMessages);
    }

    [Fact]
    public async Task Preview_OfPreview_IsRejected()
    {
        var (dispatcher, unitOfWork, vaultRoot, _, _) = CreatePipeline();
        using var _ = vaultRoot;
        var command = dispatcher.Match("/preview /preview hello");
        var context = new UnitOfWorkVaultOperationContext(unitOfWork, vaultRoot.Path);

        var result = await command.HandleAsync("/preview /preview hello", context);

        Assert.IsType<CommandResult.Rejected>(result);
    }

    [Fact]
    public async Task Preview_OfMalformedAppendTarget_ReturnsRejected_NotThrown()
    {
        var (dispatcher, unitOfWork, vaultRoot, _, _) = CreatePipeline();
        using var _ = vaultRoot;
        vaultRoot.WriteFile("", "Scratch.md", "---\nthis: [is, not, closed\n---\n\nbody\n");

        var command = dispatcher.Match("/preview /append more");
        var context = new UnitOfWorkVaultOperationContext(unitOfWork, vaultRoot.Path);

        var result = await command.HandleAsync("/preview /append more", context);

        var rejected = Assert.IsType<CommandResult.Rejected>(result);
        Assert.Contains("Preview failed", rejected.Reason);
    }
}
