using MindBot.Core.Options;
using Microsoft.Extensions.Options;

namespace MindBot.Bot.Services;

public sealed class TelegramAuthorization(IOptions<TelegramOptions> options)
{
    private readonly IReadOnlySet<long> _allowedUserIds = options.Value.ParseAllowedUserIds();

    public bool IsAuthorized(long userId) => _allowedUserIds.Contains(userId);
}
