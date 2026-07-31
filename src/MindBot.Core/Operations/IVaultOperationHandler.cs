namespace MindBot.Core.Operations;

/// <summary>Resolves one concrete <see cref="IVaultOperation"/> type into its final write. See IVaultOperation.cs for why this shape was chosen.</summary>
public interface IVaultOperationHandler
{
    bool CanHandle(IVaultOperation operation);

    Task<ResolvedWrite> ResolveAsync(IVaultOperation operation, IVaultOperationContext context, CancellationToken cancellationToken = default);
}
