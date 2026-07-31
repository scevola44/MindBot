namespace MindBot.Core.Durability;

/// <summary>Read/complete side of the durable write-job queue, used by the drain worker.</summary>
public interface IWriteJobQueue
{
    /// <summary>Pending jobs in insertion order, capped at <paramref name="maxCount"/>.</summary>
    Task<IReadOnlyList<WriteJob>> GetPendingAsync(int maxCount, CancellationToken cancellationToken = default);

    Task<int> GetPendingCountAsync(CancellationToken cancellationToken = default);

    Task MarkCompletedAsync(IReadOnlyCollection<long> jobIds, CancellationToken cancellationToken = default);
}
