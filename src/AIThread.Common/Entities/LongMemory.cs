using AIThread.Common.Enums;

namespace AIThread.Common.Entities;

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

    // Display label "Module · Layers · Kind | Keyword, ..." (MemoryLabels.Compose), kept so older AIT versions sharing
    // the store can still read new memories. Rows not migrated yet (LabelVersion null) hold free keywords here.
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // The structured label (see MemoryLabels): first search layer Module/Layers/Kind, second layer Keywords (CSV).
    // Area is set only in a repository that holds several apps. Module is the main module; ExtraModules (CSV) the other
    // modules the same fact concerns, matched by the module filter and search like the main one.
    public string? Module { get; set; }
    public string? ExtraModules { get; set; }
    public string? Layers { get; set; }
    public MemoryKind? Kind { get; set; }
    public string? Area { get; set; }
    public string? Keywords { get; set; }

    // MemoryLabels.CurrentVersion once labeled; null = written by an older version, waiting for MemoryRelabelWorker.
    public int? LabelVersion { get; set; }

    // The free label the memory had before it was relabeled.
    public string? LegacyLabel { get; set; }

    // Set when the memory could not be labeled automatically: it waits in Memory → Perlu review.
    public string? ReviewReason { get; set; }
    public int? RelabelAttempts { get; set; }

    public Guid? SourceThreadId { get; set; }

    // Git of the source thread when it started (ThreadRun.GitAuthor/GitBranch/GitBaseCommit): who wrote the change, on
    // which branch, from which commit. Null for memory without a thread or outside a repository.
    public string? GitAuthor { get; set; }
    public string? GitBranch { get; set; }
    public string? GitCommit { get; set; }
    public DateTime PromotedAt { get; set; } = DateTime.UtcNow;
    public int HitCount { get; set; }
    public DateTime? LastAccessedAt { get; set; }
}
