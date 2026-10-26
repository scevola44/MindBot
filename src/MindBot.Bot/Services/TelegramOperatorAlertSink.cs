using MindBot.Core.Notifications;
using MindBot.Core.Options;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace MindBot.Bot.Services;

/// <summary>Sends operator alerts to TELEGRAM__OPERATORCHATID. Only registered when that is set.</summary>
public sealed class TelegramOperatorAlertSink(
    ITelegramBotClient botClient,
    IOptions<TelegramOptions> telegramOptions,
    ILogger<TelegramOperatorAlertSink> logger) : IOperatorAlertSink
{
    private readonly long _operatorChatId = telegramOptions.Value.OperatorChatId
        ?? throw new InvalidOperationException("TelegramOperatorAlertSink requires TELEGRAM__OPERATORCHATID.");

    public async Task SendAsync(string message, CancellationToken cancellationToken = default)
    {
        try
        {
            await botClient.SendMessage(_operatorChatId, message, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An alert that cannot be delivered must not take down the sync loop that raised it.
            logger.LogError(ex, "Could not deliver an operator alert. The alert was: {Message}", message);
        }
    }
}
