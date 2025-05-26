namespace TgSupportBot;

public enum UserStateStep
{
	None = 0,
	RequireBrief,
	RequireDescription,
	RequireLocation,
	RequireAttachments,
}