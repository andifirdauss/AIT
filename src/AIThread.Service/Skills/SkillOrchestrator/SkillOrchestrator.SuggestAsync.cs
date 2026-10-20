using AIThread.Common.DTOs;
using AIThread.Service.Models;
using AIThread.Service.Orchestration;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Skills;

public sealed partial class SkillOrchestrator
{
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
}
