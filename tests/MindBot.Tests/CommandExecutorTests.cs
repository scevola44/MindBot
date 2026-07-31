using MindBot.Core.Commands;
using MindBot.Core.Operations;
using MindBot.Core.Options;
using MindBot.Tests.Fakes;
using Microsoft.Extensions.Options;

namespace MindBot.Tests;

public class CommandExecutorTests
{
    private sealed record FixedResultCommand(CommandResult Result) : ICommand
    {
        public bool Matches(string messageText) => true;

        public Task<CommandResult> HandleAsync(string messageText, IVaultOperationContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result);
    }

    private sealed record NeverResolves : IVaultOperation;

    private sealed class ThrowsOnResolveHandler : IVaultOperationHandler
    {
        public bool CanHandle(IVaultOperation operation) => operation is NeverResolves;

        public Task<ResolvedWrite> ResolveAsync(IVaultOperation operation, IVaultOperationContext context, CancellationToken cancellationToken = default) =>
            throw new VaultOperationException("simulated failure");
    }

    private static CommandExecutor CreateExecutor(ICommand command, out InMemoryIngestUnitOfWork unitOfWork, out TempVaultRoot vaultRoot)
    {
        vaultRoot = new TempVaultRoot();
        unitOfWork = new InMemoryIngestUnitOfWork(vaultRoot.Path);
        var applier = new VaultOperationApplier([new CreateNoteHandler(), new AppendToNoteHandler(new FixedTimeProvider(DateTimeOffset.UnixEpoch)), new ThrowsOnResolveHandler()]);
        var dispatcher = new CommandDispatcher([command]);
        return new CommandExecutor(dispatcher, applier, Options.Create(new VaultOptions { Root = vaultRoot.Path }));
    }

    [Fact]
    public async Task ExecuteAsync_OperationsResult_EnqueuesOneJobPerResolvedOperation_ReturnsReply()
    {
        var frontmatter = new MindBot.Core.Notes.NoteFrontmatter { Date = "2026-07-30T09:00:00+00:00" };
        var op = new CreateNote("05 - Fleeting/note.md", frontmatter, "body");
        var command = new FixedResultCommand(new CommandResult.Operations([op], "Add note note.md", "note.md"));
        var executor = CreateExecutor(command, out var unitOfWork, out var vaultRoot);
        using var _ = vaultRoot;

        var reply = await executor.ExecuteAsync(unitOfWork, updateId: 1, chatId: 42, senderId: 7, "anything");

        Assert.Equal("note.md", reply);
        var enqueued = Assert.Single(unitOfWork.Enqueued);
        Assert.Equal("05 - Fleeting", enqueued.RelativeFolder);
        Assert.Equal("note.md", enqueued.Filename);
        Assert.Contains("body", enqueued.Content);
    }

    [Fact]
    public async Task ExecuteAsync_DirectReply_EnqueuesNothing_ReturnsText()
    {
        var command = new FixedResultCommand(new CommandResult.DirectReply("hello"));
        var executor = CreateExecutor(command, out var unitOfWork, out var vaultRoot);
        using var _ = vaultRoot;

        var reply = await executor.ExecuteAsync(unitOfWork, updateId: 1, chatId: 42, senderId: 7, "anything");

        Assert.Equal("hello", reply);
        Assert.Empty(unitOfWork.Enqueued);
    }

    [Fact]
    public async Task ExecuteAsync_Rejected_EnqueuesNothing_ReturnsReasonInReply()
    {
        var command = new FixedResultCommand(new CommandResult.Rejected("bad input"));
        var executor = CreateExecutor(command, out var unitOfWork, out var vaultRoot);
        using var _ = vaultRoot;

        var reply = await executor.ExecuteAsync(unitOfWork, updateId: 1, chatId: 42, senderId: 7, "anything");

        Assert.Contains("bad input", reply);
        Assert.Empty(unitOfWork.Enqueued);
    }

    [Fact]
    public async Task ExecuteAsync_SecondOperationThrowsPartway_EnqueuesNothingFromEitherOperation()
    {
        var frontmatter = new MindBot.Core.Notes.NoteFrontmatter { Date = "2026-07-30T09:00:00+00:00" };
        var firstOp = new CreateNote("05 - Fleeting/note.md", frontmatter, "body");
        var secondOp = new NeverResolves();
        var command = new FixedResultCommand(new CommandResult.Operations([firstOp, secondOp], "commit", "reply"));
        var executor = CreateExecutor(command, out var unitOfWork, out var vaultRoot);
        using var _ = vaultRoot;

        var reply = await executor.ExecuteAsync(unitOfWork, updateId: 1, chatId: 42, senderId: 7, "anything");

        Assert.Contains("Could not complete", reply);
        Assert.Empty(unitOfWork.Enqueued); // the first operation's resolved write must NOT have been enqueued either
    }
}
