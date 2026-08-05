using MindBot.Core.Durability;

namespace MindBot.Tests.Fakes;

/// <summary>
/// Hands out <see cref="InMemoryIngestUnitOfWork"/> instances and keeps them, so a test can assert
/// on what a component enqueued and whether it committed.
/// </summary>
public sealed class FakeIngestUnitOfWorkFactory(string? vaultRoot = null, Action<long>? onBackgroundJobCompleted = null)
    : IIngestUnitOfWorkFactory
{
    public List<InMemoryIngestUnitOfWork> Created { get; } = [];

    public InMemoryIngestUnitOfWork Last => Created[^1];

    public Task<IIngestUnitOfWork> BeginAsync(CancellationToken cancellationToken = default)
    {
        var unitOfWork = new InMemoryIngestUnitOfWork(vaultRoot, onBackgroundJobCompleted);
        Created.Add(unitOfWork);
        return Task.FromResult<IIngestUnitOfWork>(unitOfWork);
    }
}
