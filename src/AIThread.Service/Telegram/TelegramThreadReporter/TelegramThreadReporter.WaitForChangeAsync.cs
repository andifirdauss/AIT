using AIThread.Data.Messaging;

namespace AIThread.Service.Telegram;

public sealed partial class TelegramThreadReporter
{
    // Returns when an event about this thread arrives or after the idle refresh, keeping the latest planner activity.
    private static async Task<string?> WaitForChangeAsync(IEventSubscription subscription, Guid threadId, string? activity, CancellationToken cancellationToken)
    {
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        idle.CancelAfter(IdleRefresh);
        try
        {
            while (await subscription.Reader.WaitToReadAsync(idle.Token))
            {
                (var changed, activity) = Drain(subscription, threadId, activity);
                if (changed) return activity;
            }

            // The subscription was closed: fall back to polling the database.
            await Task.Delay(IdleRefresh, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
        return activity;
    }
}
