using System.Text.RegularExpressions;

namespace AIThread.Service.Telegram;

public sealed partial class TelegramApi
{
    [GeneratedRegex(@"^\d{3,}:[A-Za-z0-9_-]{30,}$")]
    private static partial Regex TokenFormat();
}
