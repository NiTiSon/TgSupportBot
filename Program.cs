using System.Reflection;
using Serilog;
using Serilog.Core;

namespace TgSupportBot;

public static class Program
{
	private const string LogPath = "./latest.log";

	public static void Main(string[] args)
	{
		try
		{
			MainAsync(args).GetAwaiter().GetResult();
		}
		catch (Exception e)
		{
			Log.Fatal(e, "Unhandled exception");
		}
		finally
		{
			Log.CloseAndFlush();
		}
	}

	private static async Task<int> MainAsync(string[] args)
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

		WriteVersion();
		try
		{
			string? token = null;

			foreach (FileInfo arg in GetTokenFileCandidates())
			{
				if (!File.Exists(arg.FullName)) continue;
				
				token = await File.ReadAllTextAsync(arg.FullName);
				break;
			}

			if (token is null)
			{
				Log.Fatal("Token file not found, candidates are:\n\t{Files}", 
					string.Join("\n\t", GetTokenFileCandidates()));
			}
			
			BotEngine metBot = new(token);
			await metBot.Start();
		}
		catch (Exception e)
		{
			Log.Logger.Fatal(e, "Uncaught exception during bot work.");
		}

		return 0;
	}

	private static void WriteVersion()
	{
		string version = typeof(Program).Assembly.GetCustomAttributes<AssemblyInformationalVersionAttribute>().First().InformationalVersion;
		Log.Information("Initializing bot, version: {Version}", version);
	}

	private static HashSet<FileInfo> GetTokenFileCandidates()
	{
		HashSet<FileInfo> files = new(capacity: 2, new FileInfoByNameComparer());

		files.Add(new FileInfo(Path.GetFullPath("./token.txt")));

		string? location = Assembly.GetEntryAssembly()?.Location;

		if (location is not null)
		{
			files.Add(new FileInfo(
				Path.GetFullPath(
					Path.Combine(
						Path.GetDirectoryName(location) ?? string.Empty,
						"token.txt"
					)
				)
			));			
		}

		return files;
	}
}