namespace MindBot.Core.Operations;

/// <summary>Resolves any registered <see cref="IVaultOperation"/> via the first matching handler. Never switches on concrete operation types itself.</summary>
public sealed class VaultOperationApplier(IEnumerable<IVaultOperationHandler> handlers)
{
    public Task<ResolvedWrite> ResolveAsync(IVaultOperation operation, IVaultOperationContext context, CancellationToken cancellationToken = default)
    {
        var handler = handlers.FirstOrDefault(h => h.CanHandle(operation))
            ?? throw new InvalidOperationException($"No {nameof(IVaultOperationHandler)} registered for {operation.GetType().Name}.");

        return handler.ResolveAsync(operation, context, cancellationToken);
    }
}
