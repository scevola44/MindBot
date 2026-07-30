using MindBot.Bot.Services;
using MindBot.Core.Git;
using MindBot.Core.Notes;
using MindBot.Core.Options;
using MindBot.Infrastructure.Git;
using MindBot.Infrastructure.Vault;
using Microsoft.Extensions.Options;
using Telegram.Bot;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOptionsWithValidateOnStart<TelegramOptions, TelegramOptionsValidator>()
    .BindConfiguration(TelegramOptions.SectionName);
builder.Services.AddOptionsWithValidateOnStart<GitOptions, GitOptionsValidator>()
    .BindConfiguration(GitOptions.SectionName);
builder.Services.AddOptionsWithValidateOnStart<VaultOptions, VaultOptionsValidator>()
    .BindConfiguration(VaultOptions.SectionName);

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddSingleton<IGitService, GitService>();
builder.Services.AddSingleton<IVaultWriter, VaultNoteWriter>();
builder.Services.AddSingleton<NotePipeline>();
builder.Services.AddSingleton<TelegramAuthorization>();

builder.Services
    .AddHttpClient("telegram_bot_client")
    .AddTypedClient<ITelegramBotClient>((httpClient, sp) =>
    {
        var token = sp.GetRequiredService<IOptions<TelegramOptions>>().Value.BotToken;
        return new TelegramBotClient(token, httpClient);
    });

// Registration order matters: the git self-check must complete before the poller starts
// accepting updates, so a misconfigured deploy fails visibly instead of dropping messages.
builder.Services.AddHostedService<GitStartupCheckHostedService>();
builder.Services.AddHostedService<TelegramPollingHostedService>();

var host = builder.Build();
host.Run();
