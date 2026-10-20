using System.Text.Json.Nodes;

namespace AiThread.Service.Skills;

public sealed partial class SkillOrchestrator
{
    // Valid 1-based indexes only, each once, score at least MinScore, best first.
    public static IReadOnlyList<SkillMatch> ParseMatches(JsonNode json, int candidateCount)
    {
        var matches = new List<SkillMatch>();
        foreach (var item in json["matches"] as JsonArray ?? [])
        {
            if (item is null || SkillAi.Int(item["index"]) is not { } index || index < 1 || index > candidateCount) continue;
            if (matches.Any(m => m.Index == index)) continue;
            var score = Math.Clamp(SkillAi.Number(item["score"]) ?? RecommendedScore, 0, 1);
            if (score < MinScore) continue;
            matches.Add(new SkillMatch(index, score, SkillAi.Text(item["reason"]) ?? string.Empty));
        }
        return matches.OrderByDescending(m => m.Score).Take(MaxSuggestions).ToList();
    }
}
