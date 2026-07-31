namespace MindBot.Core.Notifications;

/// <summary>
/// Sends operational alerts to the operator. Kept as a Core abstraction so the sync and git
/// layers can raise alerts without taking a dependency on Telegram.Bot.
/// </summary>
public interface IOperatorNotifier
{
    /// <summary>Sends unconditionally. Use for discrete events that matter every time they happen.</summary>
    Task NotifyAsync(string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends only if the latch identified by <paramref name="key"/> is not already raised. Use for
    /// ongoing conditions — a failing push must alert once, not once per message.
    /// </summary>
    Task NotifyOnceAsync(string key, string message, CancellationToken cancellationToken = default);

    /// <summary>Lowers a latch so the next occurrence of that condition alerts again.</summary>
    Task ClearAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>Well-known latch keys, so raising and clearing sites cannot drift apart.</summary>
public static class OperatorAlertKeys
{
    public const string PushFailing = "push-failing";
}
