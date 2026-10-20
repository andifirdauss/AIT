using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Skills;

public sealed partial class SkillOrchestrator
{
    private static int Score(SkillCandidate skill, IReadOnlyList<string> terms)
    {
        var name = skill.Name.ToLowerInvariant();
        var text = $"{skill.Description} {skill.Tags}".ToLowerInvariant();
        return terms.Sum(t => (name.Contains(t) ? 3 : 0) + (text.Contains(t) ? 1 : 0));
    }
}
