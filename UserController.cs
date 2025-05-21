using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using SSSR.Data;

namespace SSSR;

public sealed class UserController
{
    private readonly ConcurrentDictionary<long, UserContext> _userStates;

    public UserController()
    {
        _userStates = [];
    }

    public UserContext? GetUserContext(long id)
    {
        _userStates.TryGetValue(id, out UserContext? retusa);
        return retusa;
    }

    public void AppendContext(long id)
    {
        _userStates.TryGetValue(id, out UserContext? parentContext);
        _userStates[id] = new(parentContext);
    }

    public void AppendContext(long id, Option pressedButton)
    {
        _userStates.TryGetValue(id, out UserContext? parentContext);
        _userStates[id] = new(parentContext, pressedButton);
    }

    public void ClearContext(long id)
    {
        _userStates.TryRemove(id, out _);
    }
}

public class UserContext
{
    public UserContext? Parent { get; }

    public Option? PressedButton { get; }

    public UserContext([Optional] UserContext? parent, [Optional] Option pressedButton)
    {
        PressedButton = pressedButton;
        Parent = parent;
    }
}