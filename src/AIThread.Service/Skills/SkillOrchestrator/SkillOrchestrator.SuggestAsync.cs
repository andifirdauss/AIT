using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Models;
using AIThread.Service.Orchestration;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Skills;

public sealed partial class SkillOrchestrator
{
    public async Task<IReadOnlyList<SkillSuggestionDto>> SuggestAsync(SuggestSkillsRequest request, CancellationToken cancellationToken = default)
    {
        var prompt = request.Prompt?.Trim() ?? string.Empty;
        if (prompt.Length == 0) throw new AppValidationException(Lang.T("Tulis dulu kebutuhan node, mis. \"aku butuh kamu jadi engineer yang handal\".", "Describe what the node needs first, e.g. \"I need you to be a reliable engineer\"."));
        if (prompt.Length > MaxPromptChars) throw new AppValidationException(Lang.T($"Kebutuhan maksimal {MaxPromptChars} karakter.", $"The need is at most {MaxPromptChars} characters."));

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var all = await db.Skills.AsNoTracking()
            .Select(s => new SkillCandidate(s.Id, s.Name, s.Description, s.Tags, s.SkillSource!.Name))
            .ToListAsync(cancellationToken);
        if (all.Count == 0) throw new AppValidationException(Lang.T("Gudang skill masih kosong. Tambahkan link skill di Setting → Skill.", "The skill store is still empty. Add a skill link in Setting → Skill."));

        var candidates = Prefilter(all, prompt, MaxCandidates);
        var (json, _, _) = await ai.AskJsonAsync(AgentPrompts.SkillMatcher, BuildInput(prompt, request.Role?.ToString(), candidates), 2_000, cancellationToken);
        if (json is null) throw new AppValidationException(Lang.T("Skill Orchestrator tidak menjawab dengan format yang bisa dibaca. Coba lagi.", "The Skill Orchestrator did not answer in a readable format. Try again."));

        return ParseMatches(json, candidates.Count)
            .Select(m => (Match: m, Skill: candidates[m.Index - 1]))
            .Select(x => new SkillSuggestionDto(x.Skill.Id, x.Skill.Name, x.Skill.Description, x.Skill.SourceName, x.Match.Reason,
                Math.Round(x.Match.Score, 2), x.Match.Score >= RecommendedScore))
            .ToList();
    }
}
