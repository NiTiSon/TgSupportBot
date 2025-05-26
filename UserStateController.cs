using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace TgSupportBot;

public sealed class UserStateController
{
	private readonly Dictionary<long, UserState> _userStates = [];

	public UserState GetCurrentStateOrCreateNew(User user)
	{
		lock (_userStates)
		{
			ref UserState? state = ref CollectionsMarshal.GetValueRefOrAddDefault(_userStates, user.Id, out bool exists);

			if (!exists)
			{
				state = new UserState
				{
					User = user,
				};
			}

			if (state is null)
			{
				Log.Error("User @{UserId} state is null", user.Username);
			}
		
			return state!;
		}
	}

	public async Task Release(ITelegramBotClient botClient, User user, CancellationToken cancellationToken)
	{
		IEnumerable<Message> messages;
		lock (_userStates)
		{
			if (_userStates.TryGetValue(user.Id, out UserState? state))
			{
				messages = state.AffectedMessages;
			}
			else
			{
				return;
			}
		}
		
		foreach (Message message in messages)
		{
			await botClient.DeleteMessage(message.Chat.Id, message.Id, cancellationToken);
		}

		lock (_userStates)
		{
			_userStates.Remove(user.Id);
		}
	}
}