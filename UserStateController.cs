using System.Runtime.InteropServices;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace TgSupportBot;

public sealed class UserStateController
{
	private readonly Dictionary<long, UserState> _userStates = [];
    private readonly TelegramBotClient _botClient;

    public UserStateController(TelegramBotClient botClient)
    {
        _botClient = botClient;
    }

    public UserState GetCurrentStateOrCreateNew(User user)
	{
#if NET6_0_OR_GREATER
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
#else
		lock (_userStates)
		{
			if (!_userStates.TryGetValue(user.Id, out UserState? state))
			{
				state = new UserState
				{
					User = user,
				};
				_userStates[user.Id] = state;
			}

			return state;
		}
#endif
	}

	public async Task<bool> RemoveLastMediaMessage(User user, CancellationToken cancellationToken = default)
    {
        Message? message;
        lock (_userStates)
        {
            UserState? state = _userStates.GetValueOrDefault(user.Id);

            message = state?.PopLastMediaMessage();
        }

        if (message != null)
        {
            try
            {
                await _botClient.DeleteMessage(message.Chat.Id, message.Id, cancellationToken);
				
                return true;
            }
            catch (Exception e)
            {
	            Log.Warning(e, "Unable to delete message.");
            }
        }

        return false;
    }

	public Task Release(UserState state, CancellationToken cancellationToken = default)
		=> Release(state.User, cancellationToken);

    public async Task Release(User user, CancellationToken cancellationToken = default)
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
			await _botClient.DeleteMessage(message.Chat.Id, message.Id, cancellationToken);
		}

		lock (_userStates)
		{
			Log.Verbose("_userStates.Remove(user.Id); #= {Result}", _userStates.Remove(user.Id));
		}
	}
}