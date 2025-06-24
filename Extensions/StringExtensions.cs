namespace TgSupportBot.Extensions;

public static class StringExtensions
{

	public static string Format(this string template, params ReadOnlySpan<object?> args)
	{
#if NET6_0_OR_GREATER
		return string.Format(template, args);
#else
		return string.Format(template, args.ToArray());
#endif
	}
}
