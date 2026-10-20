namespace AIThread.Service.Telegram;

public sealed partial class TelegramApi
{
    // BotFather tokens look like "123456789:AAE…"; anything else is rejected before calling Telegram.
    public static bool IsValidTokenFormat(string? token) => token is not null && TokenFormat().IsMatch(token);
}
