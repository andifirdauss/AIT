using AIThread.Service.Telegram;

namespace AIThread.Service.Interfaces;

public interface ITelegramBot
{
    Task HandleAsync(TgUpdate update, DateTime pollingStartedAt, CancellationToken cancellationToken);
}
