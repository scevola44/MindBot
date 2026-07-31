namespace MindBot.Core.Operations;

/// <summary>
/// An operation could not be resolved for a reason the user caused (malformed frontmatter, bad
/// /preview input) rather than a bug. Callers turn this into a chat reply instead of letting it
/// propagate; anything else (e.g. a path escaping the vault root) is a real defect and is left to
/// propagate uncaught, same as today's git/filesystem errors.
/// </summary>
public sealed class VaultOperationException(string message) : Exception(message);
