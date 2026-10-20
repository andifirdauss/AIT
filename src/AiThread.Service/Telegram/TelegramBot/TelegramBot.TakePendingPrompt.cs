namespace AiThread.Service.Telegram;

public sealed partial class TelegramBot
{
    private PendingPrompt? TakePendingPrompt(long chatId) =>
        _pending.TryRemove(chatId, out var pending) && DateTime.UtcNow - pending.ReceivedAt <= PendingPromptLifetime ? pending : null;
}
