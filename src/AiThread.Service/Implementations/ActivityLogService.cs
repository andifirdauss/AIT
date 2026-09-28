using System.Threading.Channels;
using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Data.Context;
using AiThread.Data.Messaging;
using AiThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

// Buffer between log producers and the background writer, so logging never waits on the database.
public sealed class ActivityLogQueue
{
    private readonly Channel<ActivityLog> _channel = Channel.CreateBounded<ActivityLog>(new BoundedChannelOptions(10_000)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true
    });

    public ChannelReader<ActivityLog> Reader => _channel.Reader;

    public void Enqueue(ActivityLog entry) => _channel.Writer.TryWrite(entry);
}

public sealed class ActivityLogService(
    IEventBus eventBus,
    ActivityLogQueue queue,
    IDbContextFactory<AiThreadDbContext> dbFactory,
    ILogger<ActivityLogService> logger) : IActivityLogService
{
    public void Write(ActivityLevel level, string category, string message, Guid? threadId = null, Guid? projectId = null)
    {
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

    public async Task<IReadOnlyList<ActivityLogDto>> GetRecentAsync(int take, ActivityLevel? minLevel, Guid? threadId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.ActivityLogs.AsNoTracking();
        if (threadId is { } id) query = query.Where(l => l.ThreadId == id);
        if (minLevel is { } level)
        {
            // Levels are stored as strings, so compare by membership instead of by order.
            var allowed = Enum.GetValues<ActivityLevel>().Where(v => v >= level).ToList();
            query = query.Where(l => allowed.Contains(l.Level));
        }

        var rows = await query.OrderByDescending(l => l.Id).Take(Math.Clamp(take, 1, 1000)).ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    private static ActivityLogDto ToDto(ActivityLog l) =>
        new(l.Id, l.CreatedAt, l.Level, l.Category, l.Message, l.ThreadId, l.ProjectId);

    private static LogLevel ToLogLevel(ActivityLevel level) => level switch
    {
        ActivityLevel.Debug => LogLevel.Debug,
        ActivityLevel.Warning => LogLevel.Warning,
        ActivityLevel.Error => LogLevel.Error,
        _ => LogLevel.Information
    };
}
