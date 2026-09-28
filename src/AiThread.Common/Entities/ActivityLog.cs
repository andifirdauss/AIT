using AiThread.Common.Enums;

namespace AiThread.Common.Entities;

// Dashboard log line. Uses an identity key so rows stay naturally ordered.
public class ActivityLog
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ActivityLevel Level { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Guid? ThreadId { get; set; }
    public Guid? ProjectId { get; set; }
}
