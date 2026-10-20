using System.Threading.Channels;
using AIThread.Common.DTOs;

namespace AIThread.Data.Messaging;

// In-process broker feeding the SSE endpoints.
public interface IEventBus
{
    void Publish(AppEvent evt);

    IEventSubscription Subscribe(Func<AppEvent, bool>? filter = null);
}

public interface IEventSubscription : IDisposable
{
    ChannelReader<AppEvent> Reader { get; }
}
