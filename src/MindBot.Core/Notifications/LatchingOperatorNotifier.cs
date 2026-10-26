using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MindBot.Core.Notifications;

/// <summary>Delivers one operator alert somewhere. Must swallow its own delivery failures.</summary>
public interface IOperatorAlertSink
{
    Task SendAsync(string message, CancellationToken cancellationToken = default);
}

/// <summary>
/// The <see cref="IOperatorNotifier"/>: latch bookkeeping here, delivery in the configured
/// <see cref="IOperatorAlertSink"/> (Telegram operator chat, the web chat, or the log).
/// </summary>
public sealed class LatchingOperatorNotifier(IOperatorAlertSink sink, ILogger<LatchingOperatorNotifier> logger) : IOperatorNotifier
{
    private readonly ConcurrentDictionary<string, byte> _raisedLatches = new();

    public Task NotifyAsync(string message, CancellationToken cancellationToken = default) =>
        sink.SendAsync(message, cancellationToken);

    public Task NotifyOnceAsync(string key, string message, CancellationToken cancellationToken = default)
    {
        if (!_raisedLatches.TryAdd(key, 0))
        {
            logger.LogDebug("Operator alert '{Key}' is already raised; not sending it again.", key);
            return Task.CompletedTask;
        }

        return sink.SendAsync(message, cancellationToken);
    }

    public Task ClearAsync(string key, CancellationToken cancellationToken = default)
    {
        _raisedLatches.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}

/// <summary>Used when no channel can carry alerts: they still reach the container log.</summary>
public sealed class LogOperatorAlertSink(ILogger<LogOperatorAlertSink> logger) : IOperatorAlertSink
{
    public Task SendAsync(string message, CancellationToken cancellationToken = default)
    {
        logger.LogWarning("Operator alert (no alert channel configured, so not sent): {Message}", message);
        return Task.CompletedTask;
    }
}
