using Serilog;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TgSupportBot;

public sealed class BotEngine
{
    private TelegramBotClient BotClient { get; }
    private User Me { get; set; } = null!;
    private UserStateController StateController { get; }

    public BotEngine(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        BotClient = new TelegramBotClient(token);
        StateController = new UserStateController();
    }
    
    public async Task ListenForMessagesAsync()
    {
        using CancellationTokenSource cts = new();

        ReceiverOptions receiverOptions = new()
        {
            AllowedUpdates = [],
            DropPendingUpdates = true,
        };
        BotClient.StartReceiving(
            HandleUpdateAsync,
            HandlePollingErrorAsync,
            receiverOptions,
            cts.Token
        );
        
        Me = await BotClient.GetMe(cts.Token);
        Log.Information("Bot started! @{BotName}", Me.Username);
        await Task.Delay(Timeout.Infinite, cts.Token);
    }
    private async Task HandleUpdateAsync(ITelegramBotClient botClient,
        Update update, CancellationToken cancellationToken)
    {
        if (update.Message?.Chat.Type == ChatType.Supergroup)
        {
            await botClient.SetMyCommands([
                new BotCommand("/report", "Создать репорт о проблеме.")
            ], scope: BotCommandScope.AllGroupChats(), cancellationToken: cancellationToken);
        }
        if (update.Message is { } message)
        {
            await HandleMessageAsync(botClient, update, message, cancellationToken);
        }
        if (update.CallbackQuery is { } callbackQuery)
        {
            await HandleCallbackQueryAsync(botClient, callbackQuery, update, cancellationToken);
        }
    }

    private async Task HandleCallbackQueryAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, Update update, CancellationToken cancellationToken)
    {
        UserState state = StateController.GetCurrentStateOrCreateNew(callbackQuery.From);
        
        if (state.Step == UserStateStep.RequireAttachments)
        {
            long chatId = state.AffectedMessages[^1].Chat.Id;
            int threadId = state.AffectedMessages[^1].MessageThreadId!.Value;
            if (callbackQuery.Data is "Не прикреплять" or "Завершить создание репорта")
            {
                await botClient.SendMessage(chatId, $"""
                    Репорт: {state.Brief}
                    Создан: @{state.User.Username}
                    Описание: {state.Description ?? "Не предоставлено"}
                    Местоположение: {state.Location ?? "Не указано"}
                    """,
                    messageThreadId: threadId,
                    cancellationToken: cancellationToken);
                await StateController.Release(botClient, callbackQuery.From, cancellationToken);
            }
        }
    }

    private async Task HandleMessageAsync(ITelegramBotClient botClient, Update update, Message message, CancellationToken cancellationToken)
    {
        if ((message.Text == "/start" || message.Text == "/start@" + Me.Username) && message.Chat.Type == ChatType.Private )
        {
            await botClient.SendMessage(message.Chat.Id,$"Данный бот не работает в личном чате, чтобы составить репорт о проблеме: перейдите в группу по ссылке {Configuration.GroupLink}", cancellationToken: cancellationToken);
        }

        if ((message.Text == "/report" || message.Text == "/report@" + Me.Username) &&
            message.Chat.Type == ChatType.Supergroup)
        {
            UserState state = StateController.GetCurrentStateOrCreateNew(message.From!);
            Message botMessage = await botClient.SendMessage(message.Chat.Id, "Опишите свою проблему (вкратце):",
                messageThreadId: message.MessageThreadId, cancellationToken: cancellationToken);
            state.AffectedMessages.Add(botMessage);
            state.Step = UserStateStep.RequireBrief;
        }
        else if (message.Chat.Type == ChatType.Supergroup)
        {
            UserState state = StateController.GetCurrentStateOrCreateNew(message.From!);

            switch (state.Step)
            {
                case UserStateStep.None:
                    return;

                case UserStateStep.RequireBrief:
                {
                    state.Brief = message.Text!;
                    Message botMessage = await botClient.SendMessage(message.Chat.Id,
                        "Предоставьте подробное описание проблемы:", messageThreadId: message.MessageThreadId,
                        cancellationToken: cancellationToken);

                    state.AffectedMessages.Add(message);
                    state.AffectedMessages.Add(botMessage);
                    state.Step = UserStateStep.RequireDescription;
                    break;
                }
                case UserStateStep.RequireDescription:
                {
                    state.Description = message.Text!;
                    Message botMessage = await botClient.SendMessage(message.Chat.Id,
                        "Предоставте номер вашего кабинета и литеру здания:", messageThreadId: message.MessageThreadId,
                        cancellationToken: cancellationToken);

                    state.AffectedMessages.Add(message);
                    state.AffectedMessages.Add(botMessage);
                    state.Step = UserStateStep.RequireLocation;
                    break;
                }
                case UserStateStep.RequireLocation:
                {
                    state.Location = message.Text!;

                    InlineKeyboardMarkup keyboard = new(InlineKeyboardButton.WithCallbackData("Не прикреплять"));
                    Message botMessage = await botClient.SendMessage(message.Chat.Id,
                        "Вы можете прислать изображения или видео, чтобы прикрепить их к репорту.",
                        replyMarkup: keyboard,
                        messageThreadId: message.MessageThreadId,
                        cancellationToken: cancellationToken);

                    state.AffectedMessages.Add(message);
                    state.AffectedMessages.Add(botMessage);
                    state.Step = UserStateStep.RequireAttachments;
                    break;
                }
                case UserStateStep.RequireAttachments:
                {
                    if (message.Photo is not null || message.Video is not null)
                    {
                        if (message.Photo is not null)
                        {
                            state.Files.Add(message.Photo[^1]);

                        }
                        else if (message.Video is not null)
                        {
                            state.Files.Add(message.Video);
                        }
                        state.AffectedMessages.Add(message);

                        await Task.Delay(700, cancellationToken);

                        if (state.AffectedMessages.LastOrDefault() != message)
                            return;

                        if (state.LastMediaMessage != null)
                        {
                            await botClient.DeleteMessage(message.Chat.Id, state.LastMediaMessage!.Id, cancellationToken);
                            state.LastMediaMessage = null;
                        }

                        InlineKeyboardMarkup inlineKeyboard =
                            new(InlineKeyboardButton.WithCallbackData("Завершить создание репорта"));
                        Message botMessage = state.LastMediaMessage = await botClient.SendMessage(message.Chat.Id,
                            $"Вы предоставили {state.Files.OfType<PhotoSize>().Count()} изображений и {state.Files.OfType<Video>().Count()} видео, вы можете добавить ещё или завершить создание репорта нажав на соответствующие кнопки снизу",
                            replyMarkup: inlineKeyboard,
                            messageThreadId: message.MessageThreadId,
                            cancellationToken: cancellationToken);
                        
                        state.AffectedMessages.Add(botMessage);
                        
                        await StateController.Release(botClient, message.From!, cancellationToken);
                    }
                    break;
                }
                default:
                {
                    Log.Warning("Invalid state is presented");
                    state.Step = UserStateStep.None;
                    break;
                }
            }

            Log.Verbose("Current state of @{Username}: {State}", message.From!.Username, state);
        }
    }

    private static Task HandlePollingErrorAsync(ITelegramBotClient botClient,
        Exception exception, CancellationToken cancellationToken)
    {
        string errorMessage = exception switch
        {
            ApiRequestException apiRequestException
                => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
            _ => exception.ToString()
        };

        Log.Error(errorMessage);
        return Task.CompletedTask;
    }
}