using AiThread.Common.Enums;

namespace AiThread.Common.Entities;

// Memory promoted from Redis (short-term) to the database. CreatedAt keeps the original creation time.
public class LongMemory : BaseEntity
{
    // Project on the machine that wrote the memory; null for knowledge memory, or when that project is not
    // registered in this database. Scoping uses ProjectKey.
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    // Project.ProjectKey of the owning project, shared by every clone of it; null for knowledge memory.
    public string? ProjectKey { get; set; }

    // Name of the owning project when the memory was written, so memory from another developer is named even when
    // that project is not registered here.
    public string? ProjectName { get; set; }

    public MemoryType Type { get; set; }

    // Comma-separated keywords the planner scans without reading the description.
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public Guid? SourceThreadId { get; set; }
    public DateTime PromotedAt { get; set; } = DateTime.UtcNow;
    public int HitCount { get; set; }
    public DateTime? LastAccessedAt { get; set; }
}
