using System.Text.Json.Nodes;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Memory;
using AIThread.Service.Skills;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Memory;

public sealed partial class MemoryRelabeler
{
    private async Task<NewLabel?> BuildLabelAsync(string key, Candidate candidate, JsonObject answer, IReadOnlyList<CodeMapEntry> hints, CancellationToken cancellationToken)
    {
        var moduleText = SkillAi.Text(answer["module"]);
        if (moduleText is null || !MemoryLabels.TryParseKind(SkillAi.Text(answer["kind"]), out var kind)) return null;

        var type = candidate.Key == MemoryLabels.KnowledgeKey ? MemoryType.Knowledge : candidate.Type;
        if (!MemoryLabels.KindAllowedFor(type, kind)) kind = MemoryKind.Pattern;
        var (module, _) = await registry.EnsureAsync(key, moduleText, cancellationToken: cancellationToken);
        var resolved = new List<string>();
        foreach (var name in MemoryLabels.ExtraModules(MemoryAi.Strings(answer["other_modules"]), module))
            resolved.Add((await registry.EnsureAsync(key, name, cancellationToken: cancellationToken)).Name);
        var extra = MemoryLabels.ExtraModules(resolved, module);
        string[] all = [module, .. extra];

        var layers = MemoryAi.Strings(answer["layers"])
            .Select(l => MemoryLabels.TryParseLayer(l, out var layer) ? layer : (MemoryLayer?)null)
            .OfType<MemoryLayer>().Distinct().Order().ToList();
        var ofModule = hints.Where(h => all.Contains(h.Module, StringComparer.OrdinalIgnoreCase)).ToList();
        if (layers.Count == 0) layers = ofModule.Select(h => h.Layer).Distinct().Order().ToList();
        var areas = ofModule.Select(h => h.Area).Distinct().ToList();

        var keywords = MemoryLabels.Keywords(MemoryAi.Strings(answer["keywords"]), all);
        if (keywords.Count == 0) keywords = MemoryLabels.Keywords(MemoryLabels.SplitCsv(candidate.Label), all);
        if (keywords.Count == 0) return null;
        return new NewLabel(module, extra, layers, kind, areas is [{ } area] ? area : null, keywords);
    }
}
