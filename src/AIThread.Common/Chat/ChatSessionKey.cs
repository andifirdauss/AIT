using System.Text.RegularExpressions;

namespace AIThread.Common.Chat;

// The session key of aithread chat: "chat:" + 32 hex digits, chosen by the chat and sent with every prompt of the
// session. Its threads carry it as their SessionId, so the planner gets the earlier turns and the chat can resume the
// session later (-c, -r, /resume).
public static partial class ChatSessionKey
{
    public const string Prefix = "chat:";

    public static string New() => Prefix + Guid.NewGuid().ToString("N");

    public static bool IsValid(string? key) => key is not null && Pattern().IsMatch(key);

    // The first 8 hex digits, as the chat shows a session.
    public static string Short(string key) => IsValid(key) ? key[Prefix.Length..(Prefix.Length + 8)] : key;

    [GeneratedRegex("^chat:[0-9a-f]{32}$")]
    private static partial Regex Pattern();
}
