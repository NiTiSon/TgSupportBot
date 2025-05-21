using Telegram.Bot;
using Telegram.Bot.Types;
namespace SSSR;

public static class Program
{
    public static async Task<int> Main()
    {
        FileInfo tokenFile = new("./token.txt");

        string? token = null;
        if (!tokenFile.Exists)
        {
            Console.Error.WriteLine("Не предотавлен файл token.txt");
        }
        else
        {
            try
            {
                token = await File.ReadAllTextAsync(tokenFile.FullName);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
            }
        }

        if (string.IsNullOrEmpty(token))
        {
            Console.Error.WriteLine("Токен пустой");
            return -1;
        }

        TelegramBotClient botClient = new(token);

        // Create a new bot instance
        BotEngine metBot = new(botClient);

        // Listen for messages sent to the bot
        await metBot.ListenForMessagesAsync();
        return 0;
    }
}