using Telegram.Bot;
using Telegram.Bot.Types;
using TgSupportBot.Data;
using VYaml.Serialization;

namespace TgSupportBot;

public static class Program
{
    private static readonly FileInfo TokenFile = new("./token.txt");
    private static readonly FileInfo ConfigFile = new("./config.yml");

    public static async Task<int> Main()
    {
        await using FileStream configStream = ConfigFile.OpenRead();  //Чтение ямл конфига
        Config.Value = await YamlSerializer.DeserializeAsync<Config>(configStream); 
        Console.WriteLine(Config.Value);

        string? token = null;
        if (!TokenFile.Exists)
        {
            await Console.Error.WriteLineAsync("Не предотавлен файл token.txt");
        }
        else
        {
            try
            {
                token = await File.ReadAllTextAsync(TokenFile.FullName);
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync(ex.ToString());
            }
        }

        if (string.IsNullOrEmpty(token))
        {
            await Console.Error.WriteLineAsync("Токен пустой");
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