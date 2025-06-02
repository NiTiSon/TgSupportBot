using System.Collections.ObjectModel;
using Telegram.Bot.Types;

namespace TgSupportBot;

public record UserState
{
	private readonly HashSet<Message> _affectedMessages = new(capacity: 8);
	public required User User { get; init; }

	public IReadOnlySet<Message> AffectedMessages
	{
		get
		{
			lock (_affectedMessages)
			{
				return new ReadOnlySet<Message>(_affectedMessages);
			}
		}
	}

	public List<FileBase> Files { get; } = new(capacity: 4);
	public UserStateStep Step { get; set; }
	public string? Brief { get; set; }
	public string? Description { get; set; }
	public string? Location { get; set; }
	public Message? LastMediaMessage { get; set; }

	public void AffectMessage(Message message)
	{
		lock (_affectedMessages)
		{
			_affectedMessages.Add(message);
		}
	}

	public Message? PopLastMediaMessage()
	{
		lock (_affectedMessages)
		{
			if (LastMediaMessage == null) return null;
			
			_affectedMessages.Remove(LastMediaMessage);
			return LastMediaMessage;
		}
	}
}