using AiThread.Common.Enums;

namespace AiThread.Service.Memory;

public sealed partial class CodeMapper
{
    private async Task SaveFallbackAsync(string key, IReadOnlyList<FileGroup> groups, IReadOnlyList<string> areas, CancellationToken cancellationToken)
    {
        var hits = new List<CodeMapHit>();
        foreach (var group in groups)
        {
            var (module, _) = await registry.EnsureAsync(key, FallbackModule(group), cancellationToken: cancellationToken);
            hits.AddRange(group.Paths.Select(p => new CodeMapHit(p, module, CodeMapHeuristics.Layer(p) ?? MemoryLayer.Logic,
                CodeMapHeuristics.AreaOf(p, areas), CodeMapSource.Heuristic)));
        }
        await SaveAsync(key, hits, cancellationToken);
    }
}
