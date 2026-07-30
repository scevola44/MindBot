namespace MindBot.Core.Git;

public sealed record GitOperationResult(bool Success, string? ErrorMessage)
{
    public static GitOperationResult Ok { get; } = new(true, null);

    public static GitOperationResult Fail(string errorMessage) => new(false, errorMessage);
}
