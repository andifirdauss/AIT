namespace AiThread.Service.Telegram;

public sealed partial class TelegramBotRuntime
{
    public void Polled() => _snapshot = _snapshot with { LastPollAt = DateTime.UtcNow };
}
