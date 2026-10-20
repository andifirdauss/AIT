using System.Text.Json.Nodes;
using AiThread.Common.Entities;
using AiThread.Common.Memory;
using AiThread.Service.Orchestration;
using AiThread.Service.Skills;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Memory;

public sealed partial class MemoryRelabeler
{
    private async Task<(int Labeled, int Reviewed)> LabelGroupAsync(string? key, List<Candidate> candidates,
        IReadOnlyDictionary<Guid, (string? Key, string Name)> projects, CancellationToken cancellationToken)
    {
        if (key is null)
        {
            var reviewed = 0;
            foreach (var candidate in candidates)
                if (await SaveReviewAsync(candidate, "Project memory tanpa project key dan tanpa project yang terdaftar.", cancellationToken)) reviewed++;
            return (0, reviewed);
        }

        var knowledge = key == MemoryLabels.KnowledgeKey;
        var modules = await registry.ListAsync(key, cancellationToken: cancellationToken);
        var knowledgeModules = knowledge ? modules : await registry.ListAsync(MemoryLabels.KnowledgeKey, cancellationToken: cancellationToken);
        var hints = new Dictionary<Guid, IReadOnlyList<CodeMapEntry>>();
        foreach (var candidate in candidates)
            hints[candidate.Id] = knowledge ? [] : await codeMapper.LookupAsync(key, candidate.Description, 8, cancellationToken);

        var ids = candidates.Select((c, i) => (Short: $"M{i + 1}", c)).ToDictionary(x => x.Short, x => x.c, StringComparer.OrdinalIgnoreCase);
        JsonNode? json = null;
        string? failure = null;
        try
        {
            (json, _) = await ai.AskJsonAsync(AgentPrompts.MemoryRelabeler, Input(key, candidates.First().ProjectName, modules, knowledgeModules, ids, hints), MaxTokens, cancellationToken);
            if (json is null) failure = "model tidak menjawab JSON";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failure = ex.Message;
            logger.LogWarning(ex, "Relabel model call for {Key} failed.", key);
        }

        var answers = (json?["memories"] as JsonArray ?? []).OfType<JsonObject>()
            .Where(o => SkillAi.Text(o["id"]) is not null)
            .GroupBy(o => SkillAi.Text(o["id"])!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        int labeledCount = 0, reviewedCount = 0;
        foreach (var (shortId, candidate) in ids)
        {
            answers.TryGetValue(shortId, out var answer);
            if (answer is not null && SkillAi.Text(answer["review"]) is { } review)
            {
                if (await SaveReviewAsync(candidate, review, cancellationToken)) reviewedCount++;
                continue;
            }

            var label = answer is null ? null : await BuildLabelAsync(key, candidate, answer, hints[candidate.Id], cancellationToken);
            if (label is not null)
            {
                if (await SaveLabelAsync(candidate, label, cancellationToken)) labeledCount++;
            }
            else if (candidate.Attempts >= MaxAttempts)
            {
                var reason = $"Gagal dilabeli otomatis {MaxAttempts} kali" + (failure is null ? "." : $": {failure}");
                if (await SaveReviewAsync(candidate, reason, cancellationToken)) reviewedCount++;
            }
        }
        return (labeledCount, reviewedCount);
    }
}
