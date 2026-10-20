using AIThread.Service.Interfaces;

namespace AIThread.Service.Skills;

public sealed record WebSkill(string Name, string Description, string? Tags, string Content);

public sealed record WebExtraction(IReadOnlyList<WebSkill> Skills, IReadOnlyList<string> Sources);

// The skill orchestrator's look at a pulled source: tags the SKILL.md files it found, points out the skills in a
// repository without SKILL.md files, and reads skills and repository links off a web page.
public sealed partial class SkillCatalogAgent(ISkillAi ai) : ISkillCatalogAgent
{
    private const int AnnotateBatch = 40;
    private const int ParallelBatches = 4;
    private const int ExcerptChars = 1_200;
    private const int MaxTreeEntries = 500;
    private const int ReadmeChars = 8_000;
}
