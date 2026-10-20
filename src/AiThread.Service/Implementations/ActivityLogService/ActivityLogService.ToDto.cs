using AiThread.Common.DTOs;
using AiThread.Common.Entities;

namespace AiThread.Service.Implementations;

public sealed partial class ActivityLogService
{
    private static ActivityLogDto ToDto(ActivityLog l) =>
        new(l.Id, l.CreatedAt, l.Level, l.Category, l.Message, l.ThreadId, l.ProjectId);
}
