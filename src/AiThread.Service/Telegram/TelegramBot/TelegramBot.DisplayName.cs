namespace AiThread.Service.Telegram;

public sealed partial class TelegramBot
{
    private static string DisplayName(TgChat chat)
    {
        var name = string.Join(' ', new[] { chat.FirstName, chat.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (name.Length > 0) return name;
        return chat.Title ?? (chat.Username is { } username ? "@" + username : chat.Id.ToString());
    }
}
