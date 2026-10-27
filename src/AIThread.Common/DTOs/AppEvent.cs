namespace AIThread.Common.DTOs;

// Event pushed to the UI over SSE.
public sealed record AppEvent(string Type, object? Data, Guid? ThreadId, DateTime Timestamp)
{
    public static AppEvent Create(string type, object? data, Guid? threadId = null) =>
        new(type, data, threadId, DateTime.UtcNow);
}

public static class AppEventTypes
{
    public const string Log = "log";
    public const string RouterStatus = "router.status";
    public const string ThreadCreated = "thread.created";
    public const string ThreadUpdated = "thread.updated";
    public const string ThreadOutput = "thread.output";
    public const string ThreadGit = "thread.git";
    public const string TaskUpdated = "task.updated";
    public const string TaskOutput = "task.output";
    public const string MemoryChanged = "memory.changed";
    public const string ProjectChanged = "project.changed";
    public const string NodeChanged = "node.changed";
    public const string TelegramChanged = "telegram.changed";
    public const string SkillChanged = "skill.changed";
    public const string BusinessChanged = "business.changed";
    public const string Ping = "ping";
}
