namespace TgSupportBot.Extensions;

public static class StringExtensions
{
	public static string Format(this string template, params ReadOnlySpan<object?> args)
	{
		return string.Format(template, args);
	}
}
