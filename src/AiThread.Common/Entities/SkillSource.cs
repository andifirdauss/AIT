using AiThread.Common.Enums;

namespace AiThread.Common.Entities;

// A link (git repository, file or web page) skills are pulled from. Its files live offline in the skills directory,
// in a folder named after the source id.
public class SkillSource : BaseEntity
{
    public string Url { get; set; } = string.Empty;

    // owner/repo for git hosts, otherwise the host and file name.
    public string Name { get; set; } = string.Empty;

    public SkillSourceKind Kind { get; set; }

    // Branch, tag or commit from the link; null = the default branch.
    public string? Ref { get; set; }

    // Folder inside the repository from a /tree/<ref>/<path> link; null = the whole repository.
    public string? SubPath { get; set; }

    public SkillSourceStatus Status { get; set; } = SkillSourceStatus.Queued;
    public string? Error { get; set; }
    public DateTime? LastPulledAt { get; set; }
    public int SkillCount { get; set; }

    // Set for repositories found on a web page source; deleting the page source keeps them.
    public Guid? ParentSourceId { get; set; }

    public List<Skill> Skills { get; set; } = [];
}
