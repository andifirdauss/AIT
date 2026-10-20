using AiThread.Common.DTOs;
using AiThread.Data.Messaging;

namespace AiThread.Service.Telegram;

public sealed partial class TelegramThreadReporter
{
    private static (bool Changed, string? Activity) Drain(IEventSubscription subscription, Guid threadId, string? activity)
    {
        var changed = false;
        while (subscription.Reader.TryRead(out var evt))
        {
            if (evt.ThreadId != threadId) continue;
            changed = true;
            if (evt.Type == AppEventTypes.ThreadOutput && DescribePlannerActivity(evt.Data) is { } described) activity = described;
        }
        return (changed, activity);
    }
}
