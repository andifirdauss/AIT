namespace AiThread.Service.Telegram;

public sealed partial class TelegramBot
{
    public static string SessionIdFor(long chatId) => $"telegram:{chatId}";
}
