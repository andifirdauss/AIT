namespace AiThread.Common.Entities;

// One skill pulled from a source: a folder with an entry Markdown file (usually SKILL.md) and optional supporting
// files. Content is the entry file without its frontmatter, so prompts never need the disk.
public class Skill : BaseEntity
{
    public Guid SkillSourceId { get; set; }
    public SkillSource? SkillSource { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // Comma-separated keywords from the skill orchestrator, used when matching a request to skills.
    public string? Tags { get; set; }

    // Folder of the skill relative to the source folder, forward slashes; "." for the source root.
    public string RelativeDir { get; set; } = ".";

    // Entry file name inside RelativeDir; (source, RelativeDir, EntryFile) identifies the skill across re-pulls.
    public string EntryFile { get; set; } = "SKILL.md";

    public string Content { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;

    public List<NodeSkill> Nodes { get; set; } = [];
}
