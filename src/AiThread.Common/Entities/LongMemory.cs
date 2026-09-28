using AiThread.Common.Enums;

namespace AiThread.Common.Entities;

// Memory promoted from Redis (short-term) to the database. CreatedAt keeps the original creation time.
public class LongMemory : BaseEntity
{
    // Null for knowledge memory (shared across projects).
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public MemoryType Type { get; set; }

    // Comma-separated keywords the planner scans without reading the description.
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public Guid? SourceThreadId { get; set; }
    public DateTime PromotedAt { get; set; } = DateTime.UtcNow;
    public int HitCount { get; set; }
    public DateTime? LastAccessedAt { get; set; }
}
