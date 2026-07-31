using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace MindBot.Bot.Services;

/// <summary>
/// Long-polls Telegram for updates (Telegram.Bot's GetUpdates, never a webhook) and processes
/// them one at a time so note creation never races itself.
/// </summary>
public sealed class TelegramPollingHostedService(
    ITelegramBotClient botClient,
    TelegramAuthorization authorization,
    MessageRouter messageRouter,
    ILogger<TelegramPollingHostedService> logger) : BackgroundService
{
    private static readonly UpdateType[] AllowedUpdates = [UpdateType.Message];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var me = await botClient.GetMe(stoppingToken);
        logger.LogInformation("Telegram long-polling started as @{Username}.", me.Username);

        var offset = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            Update[] updates;
            try
            {
                updates = await botClient.GetUpdates(
                    offset: offset,
                    timeout: 30,
                    allowedUpdates: AllowedUpdates,
                    cancellationToken: stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error polling Telegram for updates; retrying shortly.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }

            foreach (var update in updates)
            {
                offset = update.Id + 1;
                await HandleUpdateAsync(update, stoppingToken);
            }
        }
    }

    private async Task HandleUpdateAsync(Update update, CancellationToken cancellationToken)
    {
        var message = update.Message;
        if (message?.Text is null || message.From is null)
        {
            return;
        }

        var senderId = message.From.Id;
        if (!authorization.IsAuthorized(senderId))
        {
            logger.LogWarning("Rejected message from unauthorised sender {SenderId}.", senderId);
            await botClient.SendMessage(message.Chat.Id, "No.", cancellationToken: cancellationToken);
            return;
        }

        try
        {
            var reply = await messageRouter.RouteAsync(message.Chat.Id, message.Text, cancellationToken);
            await botClient.SendMessage(message.Chat.Id, reply, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process message from {SenderId}.", senderId);
        }
    }
}
