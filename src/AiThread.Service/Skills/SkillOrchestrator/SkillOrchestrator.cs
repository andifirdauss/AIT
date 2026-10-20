using AiThread.Data.Context;
using AiThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Skills;

public sealed record SkillCandidate(Guid Id, string Name, string Description, string? Tags, string SourceName);

public sealed record SkillMatch(int Index, double Score, string Reason);

// "I need you to be a reliable engineer" → the skills in the store that give a node that ability, ranked by the
// skill orchestrator model. The developer picks from the suggestions; nothing is attached here.
public sealed partial class SkillOrchestrator(IDbContextFactory<AiThreadDbContext> dbFactory, ISkillAi ai) : ISkillOrchestrator
{
    public const int MaxCandidates = 150;
    public const int MaxSuggestions = 6;
    public const double MinScore = 0.4;
    public const double RecommendedScore = 0.6;
    private const int MaxPromptChars = 2_000;
    private const int DescriptionChars = 300;
}
