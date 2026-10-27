using AIThread.Common.Enums;

namespace AIThread.Common.Entities;

// A standing permission the developer gave ("allow always") for one kind of action on one target of a project. Kept
// on this machine only: another developer's machine asks its own developer.
public class ApprovalGrant : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }

    public ApprovalKind Kind { get; set; }

    // Folder, command, host:port, repository or repository|remote/branch, see ApprovalKind.
    public string Target { get; set; } = string.Empty;

    // Developer name (SystemSetting.DeveloperName or the Windows user) when the grant was given.
    public string? CreatedBy { get; set; }

    public DateTime? LastUsedAt { get; set; }
    public int UseCount { get; set; }
}
