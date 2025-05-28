using System.Threading;
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
    /// <summary>
    /// Среднее время на загрузку изображения или видео в милисекундах.
    /// </summary>
    private const int AverageTimeBetweenMediaMessages = 700;
    private const int BriefMessageMaximumLength = 256;
    private const int DescriptionMessageMaximumLength = 1024;
    private const int LocationMessageMaximumLength = 512;

    private TelegramBotClient BotClient { get; }
    private User Me { get; set; } = null!;
    private UserStateController StateController { get; }

    public BotEngine(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        BotClient = new TelegramBotClient(token);
        StateController = new UserStateController(BotClient);
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
            long chatId = state.AffectedMessages.Last().Chat.Id;
            int threadId = state.AffectedMessages.Last().MessageThreadId!.Value;
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

                await SendAldumFromState(chatId, threadId, state, cancellationToken);

                if (DateTime.Now.Hour is >= 18 or < 9)
                {
                    await botClient.SendMessage(chatId, """
                        ⚠ ВНИМАНИЕ ⚠
                        В текущее время услуги технической поддержики не предоставляются!

                        Ваша заявка будет рассмотрена позже!

                        Активные часы работы тех. поддержки от 9:00 до 18:00
                        """,
                        messageThreadId: threadId,
                        cancellationToken: cancellationToken);
                }

                await StateController.Release(callbackQuery.From, cancellationToken);
            }
        }
    }

    private async Task SendAldumFromState(long chatId, int? threadId, UserState state, CancellationToken cancellationToken = default)
    {
        switch (state.Files.Count)
        {
            case 0:
                return;
            default:
                List<IAlbumInputMedia> album = new(capacity: 10);


                foreach (FileBase file in state.Files)
                {
                    if (file is PhotoSize photo)
                    {
                        album.Add(new InputMediaPhoto(photo));
                    }
                    else if (file is Video video)
                    {
                        album.Add(new InputMediaVideo(video));
                    }
                }

                while (album.Count > 0)
                {
                    await BotClient.SendMediaGroup(
                        chatId,
                        album.Take(10),
                        messageThreadId: threadId,
                        disableNotification: true,
                        cancellationToken: cancellationToken
                    );

                    album.RemoveRange(0, int.Min(10, album.Count));
                }


                break;
        }
    }

    private async Task HandleMessageAsync(ITelegramBotClient botClient, Update update, Message message, CancellationToken cancellationToken)
    {
        if ((message.Text == "/start" || message.Text == "/start@" + Me.Username) && message.Chat.Type == ChatType.Private )
        {
            await botClient.SendMessage(message.Chat.Id,$"Данный бот не работает в личном чате, чтобы составить репорт о проблеме: перейдите в группу.", cancellationToken: cancellationToken);
        }

        if ((message.Text == "/report" || message.Text == "/report@" + Me.Username) &&
            message.Chat.Type == ChatType.Supergroup)
        {
            UserState state = StateController.GetCurrentStateOrCreateNew(message.From!);
            Message botMessage = await botClient.SendMessage(message.Chat.Id, "Опишите свою проблему (вкратце):",
                messageThreadId: message.MessageThreadId, cancellationToken: cancellationToken);
            state.AffectedMessages.Add(botMessage);
            state.AffectedMessages.Add(message);
            state.Step = UserStateStep.RequireBrief;
        }
        else if (message.Chat.Type == ChatType.Supergroup)
        {
            UserState state = StateController.GetCurrentStateOrCreateNew(message.From!);

            Message botMessage;
            switch (state.Step)
            {
                case UserStateStep.None:
                    return;

                case UserStateStep.RequireBrief:
                    if (!await RequireMaximumLength(state, message, BriefMessageMaximumLength, cancellationToken)) return;
                    state.Brief = message.Text!;

                    if (!await RequireOnlyText(state, message, cancellationToken)) return;
                    botMessage = await botClient.SendMessage(message.Chat.Id,
                        "Предоставьте подробное описание проблемы:", messageThreadId: message.MessageThreadId,
                        cancellationToken: cancellationToken);

                    state.AffectedMessages.Add(message);
                    state.AffectedMessages.Add(botMessage);
                    state.Step = UserStateStep.RequireDescription;
                    break;
                case UserStateStep.RequireDescription:
                    if (!await RequireMaximumLength(state, message, DescriptionMessageMaximumLength, cancellationToken)) return;
                    state.Description = message.Text!;

                    if (!await RequireOnlyText(state, message, cancellationToken)) return;
                    botMessage = await botClient.SendMessage(message.Chat.Id,
                        "Предоставте номер вашего кабинета и литеру здания:", messageThreadId: message.MessageThreadId,
                        cancellationToken: cancellationToken);

                    state.AffectedMessages.Add(message);
                    state.AffectedMessages.Add(botMessage);
                    state.Step = UserStateStep.RequireLocation;
                    break;
                case UserStateStep.RequireLocation:
                    if (!await RequireMaximumLength(state, message, LocationMessageMaximumLength, cancellationToken)) return;
                    state.Location = message.Text!;

                    if (!await RequireOnlyText(state, message, cancellationToken)) return;
                    InlineKeyboardMarkup keyboard = new(InlineKeyboardButton.WithCallbackData("Не прикреплять"));
                    botMessage = await botClient.SendMessage(message.Chat.Id,
                        "Вы можете прислать изображения или видео, чтобы прикрепить их к репорту.",
                        replyMarkup: keyboard,
                        messageThreadId: message.MessageThreadId,
                        cancellationToken: cancellationToken);

                    state.AffectedMessages.Add(message);
                    state.AffectedMessages.Add(botMessage);
                    state.Step = UserStateStep.RequireAttachments;
                    break;
                case UserStateStep.RequireAttachments:
                    if (message.Photo is not null || message.Video is not null) // Добавить фото или видео при отправки пользователем
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

                        await Task.Delay(AverageTimeBetweenMediaMessages, cancellationToken);

                        if (state.AffectedMessages.LastOrDefault() != message)
                            return;

                        await StateController.RemoveLastMediaMessage(message.From!, cancellationToken);

                        InlineKeyboardMarkup inlineKeyboard =
                            new(InlineKeyboardButton.WithCallbackData("Завершить создание репорта"));
                        botMessage = state.LastMediaMessage = await botClient.SendMessage(message.Chat.Id,
                            $"Вы предоставили {state.Files.OfType<PhotoSize>().Count()} изображений и {state.Files.OfType<Video>().Count()} видео, вы можете добавить ещё или завершить создание репорта нажав на соответствующие кнопки снизу",
                            replyMarkup: inlineKeyboard,
                            messageThreadId: message.MessageThreadId,
                            cancellationToken: cancellationToken);

                        state.AffectedMessages.Add(botMessage);
                    }
                    break;
                default:
                    Log.Warning("Invalid state is presented");
                    state.Step = UserStateStep.None;
                    break;
            }
        }
    }

    private async Task<bool> RequireMaximumLength(UserState state, Message message, int maximum, CancellationToken cancellationToken = default)
    {
        if (message.Text?.Length > maximum)
        {
            state.AffectedMessages.Add(await BotClient.SendMessage(message.Chat, $"Ваше сообщение превышает допустимую длинну в {maximum} символов.", messageThreadId: message.MessageThreadId, cancellationToken: cancellationToken));
            return false;
        }

        return true;
    }

    private async Task<bool> RequireOnlyText(UserState state, Message message, CancellationToken cancellationToken = default)
    {
        if (message.Photo != null
         || message.Video != null)
        {
            state.AffectedMessages.Add(message);
            state.AffectedMessages.Add(
                await BotClient.SendMessage(message.Chat.Id,
                    "Вы сможете прикрепить фото или видео позже, вам нужно ввести только текст 🤗.",
                    messageThreadId : message.MessageThreadId,
                    cancellationToken: cancellationToken
                )
            );

            return false;
        }

        return true;
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

    private Task<Message> SendMessage(Message chatFrom, string message, CancellationToken cancellationToken = default)
    {
        return BotClient.SendMessage(chatFrom.Chat, message, messageThreadId: chatFrom.MessageThreadId, cancellationToken: cancellationToken);
    }
}