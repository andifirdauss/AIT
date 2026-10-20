namespace AiThread.Service.Skills;

public sealed partial class SkillOrchestrator
{
    // A large store is narrowed by keyword overlap first, so the model sees at most max candidates.
    public static IReadOnlyList<SkillCandidate> Prefilter(IReadOnlyList<SkillCandidate> all, string prompt, int max)
    {
        if (all.Count <= max) return all;
        var terms = prompt.ToLowerInvariant()
            .Split([' ', ',', '.', ';', ':', '/', '-', '"', '\'', '(', ')', '?', '!', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 3)
            .Distinct()
            .ToList();

        return all
            .Select((skill, order) => (skill, order, score: Score(skill, terms)))
            .OrderByDescending(x => x.score).ThenBy(x => x.order)
            .Take(max)
            .Select(x => x.skill)
            .ToList();
    }
}
