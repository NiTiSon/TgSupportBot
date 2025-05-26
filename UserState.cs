using Telegram.Bot;
using Telegram.Bot.Types;

namespace TgSupportBot;

public record UserState
{
	public User User { get; set; }
	public List<Message> AffectedMessages { get; } = new(capacity: 8);
	public List<FileBase> Files { get; } = new(capacity: 4);
	public UserStateStep Step { get; set; }
	public string Brief { get; set; }
	public string? Description { get; set; }
	public string? Location { get; set; }
	public Message? LastMediaMessage { get; set; }
}