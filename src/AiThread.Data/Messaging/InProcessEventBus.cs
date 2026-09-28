using System.Collections.Concurrent;
using System.Threading.Channels;
using AiThread.Common.DTOs;

namespace AiThread.Data.Messaging;

public sealed class InProcessEventBus : IEventBus
{
    // A slow browser tab must never stall the orchestrator, so each subscriber drops its oldest events when full.
    private const int SubscriberCapacity = 2000;

    private readonly ConcurrentDictionary<Guid, Subscription> _subscriptions = new();

    public void Publish(AppEvent evt)
    {
        foreach (var subscription in _subscriptions.Values)
        {
            if (subscription.Filter is null || subscription.Filter(evt))
                subscription.Channel.Writer.TryWrite(evt);
        }
    }

    public IEventSubscription Subscribe(Func<AppEvent, bool>? filter = null)
    {
        var channel = Channel.CreateBounded<AppEvent>(new BoundedChannelOptions(SubscriberCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = false
        });

        var subscription = new Subscription(Guid.NewGuid(), channel, filter, this);
        _subscriptions[subscription.Id] = subscription;
        return subscription;
    }

    private sealed class Subscription(Guid id, Channel<AppEvent> channel, Func<AppEvent, bool>? filter, InProcessEventBus owner)
        : IEventSubscription
    {
        public Guid Id { get; } = id;
        public Channel<AppEvent> Channel { get; } = channel;
        public Func<AppEvent, bool>? Filter { get; } = filter;
        public ChannelReader<AppEvent> Reader => Channel.Reader;

        public void Dispose()
        {
            owner._subscriptions.TryRemove(Id, out _);
            Channel.Writer.TryComplete();
        }
    }
}
