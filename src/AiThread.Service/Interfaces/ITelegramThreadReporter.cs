using AiThread.Data.Messaging;
using AiThread.Service.Telegram;

namespace AiThread.Service.Interfaces;

public interface ITelegramThreadReporter
{
    Task RunAsync(TelegramThreadContext context, IEventSubscription subscription, CancellationToken cancellationToken);
}
