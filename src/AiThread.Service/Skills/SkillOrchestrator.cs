using System.Text;
using System.Text.Json.Nodes;
using AiThread.Common.DTOs;
using AiThread.Data.Context;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Orchestration;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Skills;

public sealed record SkillCandidate(Guid Id, string Name, string Description, string? Tags, string SourceName);

public sealed record SkillMatch(int Index, double Score, string Reason);

// "I need you to be a reliable engineer" → the skills in the store that give a node that ability, ranked by the
// skill orchestrator model. The developer picks from the suggestions; nothing is attached here.
public sealed class SkillOrchestrator(IDbContextFactory<AiThreadDbContext> dbFactory, SkillAi ai) : ISkillOrchestrator
{
    public const int MaxCandidates = 150;
    public const int MaxSuggestions = 6;
    public const double MinScore = 0.4;
    public const double RecommendedScore = 0.6;
    private const int MaxPromptChars = 2_000;
    private const int DescriptionChars = 300;

    public async Task<IReadOnlyList<SkillSuggestionDto>> SuggestAsync(SuggestSkillsRequest request, CancellationToken cancellationToken = default)
    {
        var prompt = request.Prompt?.Trim() ?? string.Empty;
        if (prompt.Length == 0) throw new AppValidationException("Tulis dulu kebutuhan node, mis. \"aku butuh kamu jadi engineer yang handal\".");
        if (prompt.Length > MaxPromptChars) throw new AppValidationException($"Kebutuhan maksimal {MaxPromptChars} karakter.");

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var all = await db.Skills.AsNoTracking()
            .Select(s => new SkillCandidate(s.Id, s.Name, s.Description, s.Tags, s.SkillSource!.Name))
            .ToListAsync(cancellationToken);
        if (all.Count == 0) throw new AppValidationException("Gudang skill masih kosong. Tambahkan link skill di Setting → Skill.");

        var candidates = Prefilter(all, prompt, MaxCandidates);
        var (json, _, _) = await ai.AskJsonAsync(AgentPrompts.SkillMatcher, BuildInput(prompt, request.Role?.ToString(), candidates), 2_000, cancellationToken);
        if (json is null) throw new AppValidationException("Skill Orchestrator tidak menjawab dengan format yang bisa dibaca. Coba lagi.");

        return ParseMatches(json, candidates.Count)
            .Select(m => (Match: m, Skill: candidates[m.Index - 1]))
            .Select(x => new SkillSuggestionDto(x.Skill.Id, x.Skill.Name, x.Skill.Description, x.Skill.SourceName, x.Match.Reason,
                Math.Round(x.Match.Score, 2), x.Match.Score >= RecommendedScore))
            .ToList();
    }

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

    private static int Score(SkillCandidate skill, IReadOnlyList<string> terms)
    {
        var name = skill.Name.ToLowerInvariant();
        var text = $"{skill.Description} {skill.Tags}".ToLowerInvariant();
        return terms.Sum(t => (name.Contains(t) ? 3 : 0) + (text.Contains(t) ? 1 : 0));
    }

    private static string BuildInput(string prompt, string? role, IReadOnlyList<SkillCandidate> candidates)
    {
        var builder = new StringBuilder();
        builder.Append("# Developer request\n").Append(prompt).Append("\n\n");
        builder.Append("# Node role\n").Append(role ?? "(not chosen yet)").Append("\n\n");
        builder.Append("# Skills in the store\n");
        for (var i = 0; i < candidates.Count; i++)
        {
            var skill = candidates[i];
            var description = skill.Description.Length <= DescriptionChars ? skill.Description : skill.Description[..DescriptionChars] + "…";
            builder.Append($"[{i + 1}] {skill.Name} ({skill.SourceName}) — {description}");
            if (!string.IsNullOrWhiteSpace(skill.Tags)) builder.Append($" [tags: {skill.Tags}]");
            builder.Append('\n');
        }
        return builder.ToString();
    }
}
