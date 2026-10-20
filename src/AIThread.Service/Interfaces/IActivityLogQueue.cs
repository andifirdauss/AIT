using System.Threading.Channels;
using AIThread.Common.Entities;

namespace AIThread.Service.Interfaces;

public interface IActivityLogQueue
{
    ChannelReader<ActivityLog> Reader { get; }

    void Enqueue(ActivityLog entry);
}
