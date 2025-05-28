using Serilog;

namespace TgSupportBot;

public static class Program
{
    private const string LogPath = "./latest.log";
    private static readonly FileInfo TokenFile = new("./token.txt");

    public static async Task<int> Main()
    {
        if (File.Exists(LogPath))
        {
            File.Delete(LogPath);
        }
        Log.Logger = new LoggerConfiguration()
            #if DEBUG
            .MinimumLevel.Verbose()
            #endif
            .WriteTo.Console()
            .WriteTo.File(LogPath)
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