using AIThread.Common.DTOs;
using AIThread.Common.Entities;

namespace AIThread.Service.Implementations;

public sealed partial class ActivityLogService
{
    private static ActivityLogDto ToDto(ActivityLog l) =>
        new(l.Id, l.CreatedAt, l.Level, l.Category, l.Message, l.ThreadId, l.ProjectId);
}
