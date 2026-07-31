namespace MindBot.Core.Operations;

// EXTENSIBILITY: each vault operation is pure data; the logic that turns it into file content
// lives in a paired IVaultOperationHandler, discovered via DI (IEnumerable<IVaultOperationHandler>)
// and selected by CanHandle(operation) at resolve time -- mirroring ICommand's own DI-discovered,
// predicate-matched dispatch in MindBot.Core.Commands. Rejected alternatives:
//   - apply logic on the operation itself: would force operations to carry IVaultOperationContext
//     plumbing, blurring "pure data" with "behavior."
//   - double dispatch (Accept/Visit): requires editing every EXISTING operation's file to add a new
//     Visit overload whenever a new operation type is added -- the one path this architecture must
//     never require touching.
// Adding operation #3 requires only: its own record file, its own handler file, and one DI line
// (services.AddSingleton<IVaultOperationHandler, ThirdOpHandler>()). VaultOperationApplier and the
// command dispatcher are never touched.

/// <summary>Marker for a vault operation: pure data describing a change to one note.</summary>
public interface IVaultOperation;

/// <summary>
/// One operation resolved to its final form: the exact (folder, filename, content) an
/// <see cref="IVaultOperationContext"/>-backed write job should carry.
/// </summary>
public sealed record ResolvedWrite(string RelativeFolder, string Filename, string Content);
