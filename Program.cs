using Telegram.Bot;
using VYaml.Serialization;
using TgSupportBot.Controllers;
using TgSupportBot.Data;

namespace TgSupportBot;

public static class Program
{
    private static readonly FileInfo TokenFile = new("./token.txt");
    private static readonly FileInfo ConfigFile = new("./config.yml");

    public static async Task<int> Main()
    {
        using FileStream configStream = ConfigFile.OpenRead();  //Чтение ямл конфига
        Config.Value = await YamlSerializer.DeserializeAsync<Config>(configStream); 
        Console.WriteLine(Config.Value);

        string? token = null;
        if (!TokenFile.Exists)
        {
            Console.Error.WriteLine("Не предотавлен файл token.txt");
        }
        else
        {
            try
            {
                token = await File.ReadAllTextAsync(TokenFile.FullName);
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