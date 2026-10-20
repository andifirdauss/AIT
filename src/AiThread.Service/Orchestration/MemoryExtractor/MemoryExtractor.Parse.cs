using System.Text.Json.Nodes;
using AiThread.Common.Enums;
using AiThread.Common.Memory;
using AiThread.Service.Memory;
using AiThread.Service.Skills;

namespace AiThread.Service.Orchestration;

public sealed partial class MemoryExtractor
{
    private static List<ExtractedMemory> Parse(string content)
    {
        var root = SkillAi.ParseObject(content);
        var list = new List<ExtractedMemory>();
        foreach (var item in (root?["memories"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var description = SkillAi.Text(item["description"]);
            var module = SkillAi.Text(item["module"]);
            if (description is null || module is null || !MemoryLabels.TryParseKind(SkillAi.Text(item["kind"]), out var kind)) continue;

            var type = string.Equals(SkillAi.Text(item["type"]), "knowledge", StringComparison.OrdinalIgnoreCase) ? MemoryType.Knowledge : MemoryType.Project;
            var layers = MemoryAi.Strings(item["layers"])
                .Select(l => MemoryLabels.TryParseLayer(l, out var layer) ? layer : (MemoryLayer?)null)
                .OfType<MemoryLayer>()
                .Distinct()
                .ToList();
            var keywords = MemoryAi.Strings(item["keywords"]);
            if (keywords.Count == 0) continue;
            list.Add(new ExtractedMemory(type, module, MemoryAi.Strings(item["other_modules"]), layers, kind, keywords, description));
        }
        return list;
    }
}
