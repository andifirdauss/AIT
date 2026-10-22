using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class ActivityLogService
{
    public void Write(ActivityLevel level, string category, string message, Guid? threadId = null, Guid? projectId = null)
    {
        message = secrets.Redact(message);
        var entry = new ActivityLog
        {
            CreatedAt = DateTime.UtcNow,
            Level = level,
            Category = category,
            Message = message.Length > 4000 ? message[..4000] : message,
            ThreadId = threadId,
            ProjectId = projectId
        };

        logger.Log(ToLogLevel(level), "[{Category}] {Message}", category, entry.Message);
        eventBus.Publish(AppEvent.Create(AppEventTypes.Log, ToDto(entry), threadId));
        queue.Enqueue(entry);
    }
}
