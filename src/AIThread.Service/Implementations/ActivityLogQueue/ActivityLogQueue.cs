using System.Threading.Channels;
using AIThread.Common.Entities;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Implementations;

// Buffer between log producers and the background writer, so logging never waits on the database.
public sealed partial class ActivityLogQueue : IActivityLogQueue
{
    private readonly Channel<ActivityLog> _channel = Channel.CreateBounded<ActivityLog>(new BoundedChannelOptions(10_000)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true
    });

    public ChannelReader<ActivityLog> Reader => _channel.Reader;
}
