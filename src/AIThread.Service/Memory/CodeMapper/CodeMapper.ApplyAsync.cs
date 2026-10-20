using System.Text.Json.Nodes;
using AIThread.Common.Enums;
using AIThread.Common.Memory;
using AIThread.Service.Skills;

namespace AIThread.Service.Memory;

public sealed partial class CodeMapper
{
    // Saves a batch; returns the number of files saved.
    private async Task<int> ApplyAsync(string key, FileGroup[] batch, IReadOnlyDictionary<string, JsonObject> answers, IReadOnlyList<string> areas,
        CancellationToken cancellationToken)
    {
        var hits = new List<CodeMapHit>();
        foreach (var group in batch)
        {
            answers.TryGetValue(group.Id, out var answer);
            var moduleName = SkillAi.Text(answer?["module"]) ?? FallbackModule(group);
            var (module, _) = await registry.EnsureAsync(key, moduleName, SkillAi.Text(answer?["description"]),
                MemoryAi.Strings(answer?["aliases"]), cancellationToken);
            var layers = answer?["layers"] as JsonObject;
            foreach (var path in group.Paths)
            {
                var layer = CodeMapHeuristics.Layer(path)
                            ?? (MemoryLabels.TryParseLayer(SkillAi.Text(layers?[path]), out var byModel) ? byModel : MemoryLayer.Logic);
                hits.Add(new CodeMapHit(path, module, layer, CodeMapHeuristics.AreaOf(path, areas), answer is null ? CodeMapSource.Heuristic : CodeMapSource.AI));
            }
        }
        await SaveAsync(key, hits, cancellationToken);
        return hits.Count;
    }
}
