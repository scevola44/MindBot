using MindBot.Core.Durability;
using MindBot.Core.Health;
using MindBot.Core.Ingest;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace MindBot.Bot.Services;

/// <summary>
/// Long-polls Telegram for updates (Telegram.Bot's GetUpdates, never a webhook) and turns each one
/// into a durably queued write job.
/// <para>
/// This loop deliberately does no git or filesystem work. Accepting an update is a single SQLite
/// transaction — dedupe check, routing, filename reservation, job insert, offset advance — and
/// once it commits the note cannot be lost. A crash before the commit leaves no trace and Telegram
/// redelivers; a crash after it leaves the update recorded, so the redelivery is skipped. That is
/// the whole duplicate guard, and it only holds because nothing slow happens inside it.
/// </para>
/// </summary>
public sealed class TelegramPollingHostedService(
    ITelegramBotClient botClient,
    TelegramAuthorization authorization,
    MessageRouter messageRouter,
    IIngestUnitOfWorkFactory unitOfWorkFactory,
    IRepositoryStateStore repositoryStateStore,
    WriteJobSignal writeJobSignal,
    HealthSnapshot health,
    TimeProvider timeProvider,
    ILogger<TelegramPollingHostedService> logger) : BackgroundService
{
    private static readonly UpdateType[] AllowedUpdates = [UpdateType.Message];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var me = await botClient.GetMe(stoppingToken);
        logger.LogInformation("Telegram long-polling started as @{Username}.", me.Username);

        // Resuming from the stored offset avoids re-fetching a batch that was already accepted.
        // Correctness does not depend on it — the processed-update check would skip them anyway.
        var state = await repositoryStateStore.GetAsync(stoppingToken);
        var offset = state.LastTelegramOffset;

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
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                continue;
            }

            health.RecordSuccessfulPoll(timeProvider.GetUtcNow());

            foreach (var update in updates)
            {
                await HandleUpdateAsync(update, stoppingToken);

                // Advanced only after the update has been accepted, so a crash mid-handle causes a
                // redelivery rather than a silent skip.
                offset = update.Id + 1;
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
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["SenderId"] = senderId });

        if (!authorization.IsAuthorized(senderId))
        {
            logger.LogWarning("Rejected message from unauthorised sender {SenderId}.", senderId);
            await TrySendAsync(message.Chat.Id, "No.", cancellationToken);
            return;
        }

        string reply;
        try
        {
            await using var unitOfWork = await unitOfWorkFactory.BeginAsync(cancellationToken);

            if (await unitOfWork.IsUpdateProcessedAsync(update.Id, cancellationToken))
            {
                logger.LogInformation(
                    "Update {UpdateId} was already processed before a restart; skipping the redelivery.",
                    update.Id);
                return;
            }

            reply = await messageRouter.RouteAsync(
                unitOfWork,
                update.Id,
                message.Chat.Id,
                senderId,
                message.Text,
                cancellationToken);

            await unitOfWork.MarkUpdateProcessedAsync(update.Id, cancellationToken);
            await unitOfWork.SetTelegramOffsetAsync(update.Id + 1, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to accept update {UpdateId}; it will be redelivered.", update.Id);
            return;
        }

        writeJobSignal.Signal();

        // The note is durable at this point. Failing to deliver the confirmation costs the user a
        // reply, not the capture, so it must not abort anything.
        await TrySendAsync(message.Chat.Id, reply, cancellationToken);
    }

    private async Task TrySendAsync(long chatId, string text, CancellationToken cancellationToken)
    {
        try
        {
            await botClient.SendMessage(chatId, text, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not send a reply to chat {ChatId}.", chatId);
        }
    }
}
