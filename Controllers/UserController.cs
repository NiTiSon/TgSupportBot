using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using TgSupportBot.Data;
using TgSupportBot;

namespace TgSupportBot.Controllers;

public sealed class UserController
{
    private readonly ConcurrentDictionary<long, UserContext> _userStates;

    public UserController(Config config)
    {
        _userStates = [];
    }
}

public class UserContext
{
    public UserContext? Parent { get; }

    public long MessageId { get; }

    public Option? PressedButton { get; }

    public UserContext(long messageId, [Optional] UserContext? parent, [Optional] Option pressedButton)
    {
        PressedButton = pressedButton;
        Parent = parent;
    }
}