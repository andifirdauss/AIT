using AiThread.Common.DTOs;

namespace AiThread.Service.Implementations;

public sealed partial class SkillService
{
    private static int Rank(SkillDto skill, string[] terms)
    {
        if (terms.All(t => skill.Name.Contains(t, StringComparison.OrdinalIgnoreCase))) return 0;
        var meta = $"{skill.Name} {skill.Description} {skill.Tags} {skill.SourceName}";
        return terms.All(t => meta.Contains(t, StringComparison.OrdinalIgnoreCase)) ? 1 : 2;
    }
}
