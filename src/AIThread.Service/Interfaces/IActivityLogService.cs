using AIThread.Common.DTOs;
using AIThread.Common.Enums;

namespace AIThread.Service.Interfaces;

// Dashboard log: pushed live over SSE and persisted in the background.
public interface IActivityLogService
{
    void Write(ActivityLevel level, string category, string message, Guid? threadId = null, Guid? projectId = null);

    Task<IReadOnlyList<ActivityLogDto>> GetRecentAsync(int take, ActivityLevel? minLevel, Guid? threadId, CancellationToken cancellationToken = default);
}

public static class ActivityLogServiceExtensions
{
    public static void Info(this IActivityLogService log, string category, string message, Guid? threadId = null, Guid? projectId = null) =>
        log.Write(ActivityLevel.Info, category, message, threadId, projectId);

    public static void Success(this IActivityLogService log, string category, string message, Guid? threadId = null, Guid? projectId = null) =>
        log.Write(ActivityLevel.Success, category, message, threadId, projectId);

    public static void Warn(this IActivityLogService log, string category, string message, Guid? threadId = null, Guid? projectId = null) =>
        log.Write(ActivityLevel.Warning, category, message, threadId, projectId);

    public static void Error(this IActivityLogService log, string category, string message, Guid? threadId = null, Guid? projectId = null) =>
        log.Write(ActivityLevel.Error, category, message, threadId, projectId);

    public static void Debug(this IActivityLogService log, string category, string message, Guid? threadId = null, Guid? projectId = null) =>
        log.Write(ActivityLevel.Debug, category, message, threadId, projectId);
}
