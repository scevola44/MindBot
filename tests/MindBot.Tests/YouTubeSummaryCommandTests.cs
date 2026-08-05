using System.Text.Json;
using MindBot.Core.Commands;
using MindBot.Core.Durability;
using MindBot.Core.Operations;
using MindBot.Core.Options;
using MindBot.Tests.Fakes;
using Microsoft.Extensions.Options;

namespace MindBot.Tests;

public sealed class YouTubeSummaryCommandTests
{
    private static YouTubeSummaryCommand CreateCommand(string baseUrl = "https://n8n.example/webhook") =>
        new(Options.Create(new N8nOptions { BaseUrl = baseUrl }));

    private static IVaultOperationContext Context(TempVaultRoot vaultRoot) =>
        new UnitOfWorkVaultOperationContext(new InMemoryIngestUnitOfWork(vaultRoot.Path), vaultRoot.Path);

    [Fact]
    public void MatchesTheCommandRegardlessOfCase()
    {
        var command = CreateCommand();

        Assert.True(command.Matches("/ytsummary https://youtu.be/qIeJ7Gw9v_I"));
        Assert.True(command.Matches("/YTSummary https://youtu.be/qIeJ7Gw9v_I"));
        Assert.True(command.Matches("/ytsummary@MindBot https://youtu.be/qIeJ7Gw9v_I"));
        Assert.False(command.Matches("/task something"));
        Assert.False(command.Matches("just some text"));
    }

    [Fact]
    public async Task QueuesAJobCarryingTheVideoIdAndNoChunkCount()
    {
        using var vaultRoot = new TempVaultRoot();

        var result = await CreateCommand().HandleAsync("/ytsummary https://youtu.be/qIeJ7Gw9v_I?si=abc", Context(vaultRoot));

        var deferred = Assert.IsType<CommandResult.DeferredJob>(result);
        Assert.Equal(BackgroundJobKinds.YouTubeSummary, deferred.Kind);

        var payload = YouTubeSummaryCommand.ParsePayload(deferred.Payload);
        Assert.Equal("qIeJ7Gw9v_I", payload.VideoId);
        Assert.Null(payload.ChunkCount);
    }

    [Fact]
    public async Task CarriesAnExplicitChunkCount()
    {
        using var vaultRoot = new TempVaultRoot();

        var result = await CreateCommand().HandleAsync("/ytsummary https://youtu.be/qIeJ7Gw9v_I 4", Context(vaultRoot));

        var deferred = Assert.IsType<CommandResult.DeferredJob>(result);
        Assert.Equal(4, YouTubeSummaryCommand.ParsePayload(deferred.Payload).ChunkCount);
    }

    /// <summary>The payload is stored in SQLite and read back after a restart, so its keys are a contract.</summary>
    [Fact]
    public async Task PayloadJsonUsesStableKeys()
    {
        using var vaultRoot = new TempVaultRoot();

        var result = await CreateCommand().HandleAsync("/ytsummary https://youtu.be/qIeJ7Gw9v_I 2", Context(vaultRoot));

        var deferred = Assert.IsType<CommandResult.DeferredJob>(result);
        using var document = JsonDocument.Parse(deferred.Payload);
        Assert.Equal("qIeJ7Gw9v_I", document.RootElement.GetProperty("videoId").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("chunkCount").GetInt32());
    }

    [Fact]
    public async Task RejectsAMissingArgument()
    {
        using var vaultRoot = new TempVaultRoot();

        var result = await CreateCommand().HandleAsync("/ytsummary", Context(vaultRoot));

        Assert.Contains("Usage:", Assert.IsType<CommandResult.Rejected>(result).Reason);
    }

    [Fact]
    public async Task RejectsSomethingThatIsNotAYouTubeLink()
    {
        using var vaultRoot = new TempVaultRoot();

        var result = await CreateCommand().HandleAsync("/ytsummary https://vimeo.com/12345", Context(vaultRoot));

        Assert.Contains("not a YouTube video link", Assert.IsType<CommandResult.Rejected>(result).Reason);
    }

    [Fact]
    public async Task RejectsAnOutOfRangeOrNonNumericChunkCount()
    {
        using var vaultRoot = new TempVaultRoot();
        var command = CreateCommand();

        foreach (var argument in new[] { "0", "13", "-1", "lots" })
        {
            var result = await command.HandleAsync($"/ytsummary https://youtu.be/qIeJ7Gw9v_I {argument}", Context(vaultRoot));
            Assert.Contains("chunk count", Assert.IsType<CommandResult.Rejected>(result).Reason);
        }
    }

    [Fact]
    public async Task RejectsEverythingWhenN8nIsNotConfigured()
    {
        using var vaultRoot = new TempVaultRoot();

        var result = await CreateCommand(baseUrl: string.Empty)
            .HandleAsync("/ytsummary https://youtu.be/qIeJ7Gw9v_I", Context(vaultRoot));

        Assert.Contains("N8N__BASEURL", Assert.IsType<CommandResult.Rejected>(result).Reason);
    }

    /// <summary>Accepting a video must not touch the vault: the note does not exist until the pipeline finishes.</summary>
    [Fact]
    public async Task ReservesNoFilenameAndQueuesNoWrite()
    {
        using var vaultRoot = new TempVaultRoot();
        var unitOfWork = new InMemoryIngestUnitOfWork(vaultRoot.Path);

        await CreateCommand().HandleAsync(
            "/ytsummary https://youtu.be/qIeJ7Gw9v_I",
            new UnitOfWorkVaultOperationContext(unitOfWork, vaultRoot.Path));

        Assert.Empty(unitOfWork.Enqueued);
    }
}
