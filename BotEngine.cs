using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TgSupportBot.Data;
using TgSupportBot.Controllers;

namespace TgSupportBot;

public class BotEngine
{
    private readonly TelegramBotClient _botClient;
    private readonly UserController _users;

    public BotEngine(TelegramBotClient botClient)
    {
        _botClient = botClient;
        _users = new UserController(Config.Value);
    }
    // Create a listener so that we can wait for messages to be sent to the bot
    public async Task ListenForMessagesAsync()
    {
        using var cts = new CancellationTokenSource();

        ReceiverOptions receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [], // receive all update types
        };
        _botClient.StartReceiving(
            HandleUpdateAsync,
            HandlePollingErrorAsync,
            receiverOptions,
            cts.Token
        );

        var me = await _botClient.GetMe();

        Console.WriteLine($"=== BOT INITIALIZED @{me.Username} ===");
        Console.ReadLine();
    }
    private async Task HandleUpdateAsync(ITelegramBotClient botClient,
        Update update, CancellationToken cancellationToken)
    {
        if (update.Message is Message message)
        {
            await HandleMessageAsync(botClient, update, message, cancellationToken);
        }
    }

    private async Task HandleMessageAsync(ITelegramBotClient botClient, Update update, Message message, CancellationToken cancellationToken)
    {
        if (message.Text == "/start")
        {
            await botClient.SetMyCommands([
                new BotCommand("/start", "Перезапустить бота")
            ], cancellationToken: cancellationToken);
            await botClient.SendMessage(message.Chat.Id,
            """
            Выберите 
            """);
        }
    }

    private async Task OnStartHandle(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        //Inline keybord
        List<List<InlineKeyboardButton>> root = new(capacity: 4);
        foreach (Option option in Config.Value.Options)
        {
            root.Add([InlineKeyboardButton.WithCallbackData(option.Text)]);
        }

        //Reply keyboard
        
        InlineKeyboardMarkup replyKeyboard = new(root);

        await botClient.SendMessage(
            update.Message!.Chat.Id,
            "Выберете категорию описывающую вашу проблему:",
            replyMarkup: replyKeyboard,
            cancellationToken: cancellationToken);
    }

    private Task HandlePollingErrorAsync(ITelegramBotClient botClient,
        Exception exception, CancellationToken cancellationToken)
    {
        string errorMessage = exception switch
        {
            ApiRequestException apiRequestException
                => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
            _ => exception.ToString()
        };

        Console.WriteLine(errorMessage);
        return Task.CompletedTask;
    }
}