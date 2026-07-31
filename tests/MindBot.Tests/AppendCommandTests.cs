using MindBot.Core.Commands;
using MindBot.Core.Operations;
using MindBot.Tests.Fakes;

namespace MindBot.Tests;

public class AppendCommandTests
{
    private static IVaultOperationContext CreateContext(TempVaultRoot vaultRoot) =>
        new UnitOfWorkVaultOperationContext(new InMemoryIngestUnitOfWork(vaultRoot.Path), vaultRoot.Path);

    [Theory]
    [InlineData("/append hello")]
    [InlineData("/APPEND hello")]
    [InlineData("/append@mybot hello")]
    public void Matches_AppendVariants_ReturnTrue(string text)
    {
        Assert.True(new AppendCommand().Matches(text));
    }

    [Fact]
    public void Matches_OtherText_ReturnsFalse()
    {
        Assert.False(new AppendCommand().Matches("hello"));
        Assert.False(new AppendCommand().Matches("/appendix hello"));
    }

    [Fact]
    public async Task HandleAsync_ProducesOneAppendToNote_AgainstScratchMd()
    {
        using var vaultRoot = new TempVaultRoot();
        var command = new AppendCommand();

        var result = await command.HandleAsync("/append Buy milk", CreateContext(vaultRoot));

        var ops = Assert.IsType<CommandResult.Operations>(result);
        var op = Assert.IsType<AppendToNote>(Assert.Single(ops.Items));
        Assert.Equal("Scratch.md", op.Path);
        Assert.Equal("Buy milk", op.Content);
        Assert.Equal("Appended to Scratch.md.", ops.Reply);
    }

    [Fact]
    public async Task HandleAsync_NoArgument_ReturnsRejected()
    {
        using var vaultRoot = new TempVaultRoot();
        var command = new AppendCommand();

        var result = await command.HandleAsync("/append", CreateContext(vaultRoot));

        var rejected = Assert.IsType<CommandResult.Rejected>(result);
        Assert.Contains("Usage:", rejected.Reason);
    }
}
