using MindBot.Core.Notifications;
using Microsoft.AspNetCore.Authentication;

namespace MindBot.Bot.Web;

public static class WebServiceCollectionExtensions
{
    /// <summary>The web channel's services. The state store (AddMindBotState) must be registered too.</summary>
    public static IServiceCollection AddMindBotWeb(this IServiceCollection services)
    {
        services.AddSingleton<WebMessageHub>();
        services.AddSingleton<IChannelReplySender, WebChatReplySender>();
        services.AddSingleton<WebMessageService>();
        services.AddSingleton<WebSocketSession>();

        services
            .AddAuthentication(DeviceTokenAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, DeviceTokenAuthenticationHandler>(DeviceTokenAuthenticationHandler.SchemeName, _ => { });
        services.AddAuthorization();

        services.ConfigureHttpJsonOptions(options => WebJson.Configure(options.SerializerOptions));

        return services;
    }
}
