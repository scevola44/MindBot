using System.Collections.Concurrent;
using MindBot.Core.Notifications;
using MindBot.Core.Options;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace MindBot.Bot.Services;

/// <summary>
/// Sends operator alerts to TELEGRAM__OPERATORCHATID. When that is not configured the alerts are
/// logged instead, so the bot runs unchanged without it.
/// </summary>
public sealed class TelegramOperatorNotifier(
    ITelegramBotClient botClient,
    IOptions<TelegramOptions> telegramOptions,
    ILogger<TelegramOperatorNotifier> logger) : IOperatorNotifier
{
    private readonly long? _operatorChatId = telegramOptions.Value.OperatorChatId;
    private readonly ConcurrentDictionary<string, byte> _raisedLatches = new();

    public Task NotifyAsync(string message, CancellationToken cancellationToken = default) =>
        SendAsync(message, cancellationToken);

    public Task NotifyOnceAsync(string key, string message, CancellationToken cancellationToken = default)
    {
        if (!_raisedLatches.TryAdd(key, 0))
        {
            logger.LogDebug("Operator alert '{Key}' is already raised; not sending it again.", key);
            return Task.CompletedTask;
        }

        return SendAsync(message, cancellationToken);
    }

    public Task ClearAsync(string key, CancellationToken cancellationToken = default)
    {
        _raisedLatches.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    private async Task SendAsync(string message, CancellationToken cancellationToken)
    {
        if (_operatorChatId is null)
        {
            logger.LogWarning("Operator alert (TELEGRAM__OPERATORCHATID not set, so not sent): {Message}", message);
            return;
        }

        try
        {
            await botClient.SendMessage(_operatorChatId.Value, message, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An alert that cannot be delivered must not take down the sync loop that raised it.
            logger.LogError(ex, "Could not deliver an operator alert. The alert was: {Message}", message);
        }
    }
}
