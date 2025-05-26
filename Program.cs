using Serilog;
using Serilog.Core;
using Telegram.Bot;

namespace TgSupportBot;

public static class Program
{
    private static readonly FileInfo TokenFile = new("./token.txt");

    public static async Task<int> Main()
    {
        Log.Logger = new LoggerConfiguration()
            #if DEBUG
            .MinimumLevel.Verbose()
            #endif
            .WriteTo.Console()
            .WriteTo.File("./latest.log")
            .CreateLogger();

        try
        {
            string token = await File.ReadAllTextAsync(TokenFile.FullName);
            
            BotEngine metBot = new(token);
            await metBot.ListenForMessagesAsync();
        }
        catch (Exception e)
        {
            Log.Logger.Fatal(e, "An error occured during bot initialization");
        }
        return 0;
    }
}