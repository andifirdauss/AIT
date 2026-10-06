using AiThread.Common.Enums;

namespace AiThread.Common.Entities;

// The state of a business page after one save (by the wiki writer or by hand), so any version can be restored.
public class BusinessPageRevision : BaseEntity
{
    public Guid PageId { get; set; }
    public int Version { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? ChangeNote { get; set; }
    public string? Author { get; set; }
    public BusinessEditKind EditKind { get; set; }

    // Threads the writer summarized into this version, comma-separated.
    public string? SourceThreadIds { get; set; }
}
