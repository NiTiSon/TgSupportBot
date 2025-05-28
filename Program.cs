using Serilog;

namespace TgSupportBot;

public static class Program
{
	private const string LogPath = "./latest.log";
	private static readonly FileInfo TokenFile = new("./token.txt");

	public static async Task<int> Main(string[] args)
	{
		if (File.Exists(LogPath) && !args.Contains("--log-append"))
		{
			File.Delete(LogPath);
		}
		Log.Logger = new LoggerConfiguration()
#if DEBUG
			.MinimumLevel.Verbose()
#else
			.MinimumLevel.Information()
#endif
			.WriteTo.Console()
			.WriteTo.File(LogPath)
			.CreateLogger();

		try
		{
			string token = await File.ReadAllTextAsync(TokenFile.FullName);
			
			BotEngine metBot = new(token);
			await metBot.Start();
		}
		catch (Exception e)
		{
			Log.Logger.Fatal(e, "Uncaughted exception during bot work.");
		}

		return 0;
	}
}