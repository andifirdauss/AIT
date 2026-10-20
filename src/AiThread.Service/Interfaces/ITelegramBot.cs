using AiThread.Service.Telegram;

namespace AiThread.Service.Interfaces;

public interface ITelegramBot
{
    Task HandleAsync(TgUpdate update, DateTime pollingStartedAt, CancellationToken cancellationToken);
}
