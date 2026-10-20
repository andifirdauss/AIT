using AiThread.Common.Entities;

namespace AiThread.Service.Implementations;

public sealed partial class ActivityLogQueue
{
    public void Enqueue(ActivityLog entry) => _channel.Writer.TryWrite(entry);
}
