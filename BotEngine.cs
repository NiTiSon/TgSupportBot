using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TgSupportBot.Data;

namespace TgSupportBot;

public class BotEngine
{
    private readonly TelegramBotClient _botClient;
    private readonly UserController _users;

    public BotEngine(TelegramBotClient botClient)
    {
        _botClient = botClient;
        _users = new UserController();
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

        Console.WriteLine($"Start listening for @{me.Username}");
        Console.ReadLine();
    }
    private async Task HandleUpdateAsync(ITelegramBotClient botClient,
        Update update, CancellationToken cancellationToken)
    {
        if (update.CallbackQuery is { } query)
        {
            UserContext? context = _users.GetUserContext(query.From.Id);
            
            if (context is null) // После вызова /start
            {
                Option? selectedOption = Config.Value.Options?.FirstOrDefault(t => t.Text == query.Data);

                _users.AppendContext(query.From.Id, selectedOption!.Value);
            }
            else if (context.PressedButton is {} option)
            {
                Option? selectedOption = option.Options?.FirstOrDefault(t => t.Text == query.Data);

                if (selectedOption is null)
                {
                    await botClient.SendMessage(query.Message!.Chat.Id, "Данное сообщение уже устарело.", cancellationToken: cancellationToken);
                    return;
                }

                _users.AppendContext(query.From.Id, selectedOption.Value);
            }

            return;
        }

        // Only process Message updates
        if (update.Message is not { } message)
        {
            return;
        }

        // Only process text messages
        if (message.Text is not { } messageText)
        {
            return;
        }

        //Log in console
        Console.WriteLine($"Received a '{messageText}' message in chat {message.Chat.Id}.");

        if (message.Text == "/start")
        {
            _users.ClearContext(message.From!.Id);
            await OnStartHandle(botClient, update, cancellationToken);
            return;
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