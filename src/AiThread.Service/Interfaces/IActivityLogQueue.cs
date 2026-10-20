using System.Threading.Channels;
using AiThread.Common.Entities;

namespace AiThread.Service.Interfaces;

public interface IActivityLogQueue
{
    ChannelReader<ActivityLog> Reader { get; }

    void Enqueue(ActivityLog entry);
}
