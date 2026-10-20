using AIThread.Data.Messaging;
using AIThread.Service.Telegram;

namespace AIThread.Service.Interfaces;

public interface ITelegramThreadReporter
{
    Task RunAsync(TelegramThreadContext context, IEventSubscription subscription, CancellationToken cancellationToken);
}
