using AiThread.Service.Models;

namespace AiThread.Service.Memory;

public sealed partial class CodeMapper
{
    // Stored entries plus the files the rules are sure about (saved on the way); the rest is returned as unsure.
    public async Task<(IReadOnlyList<CodeMapHit> Hits, IReadOnlyList<string> Unsure)> QuickAsync(
        ProjectContext project, IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        var wanted = Normalize(project, paths);
        if (wanted.Count == 0 || string.IsNullOrEmpty(project.Key)) return ([], []);

        var stored = await StoredAsync(project.Key, wanted, cancellationToken);
        var hits = stored.Values.Select(ToHit).ToList();
        var missing = wanted.Where(p => !stored.ContainsKey(p)).ToList();
        if (missing.Count == 0) return (hits, []);

        var modules = await registry.ListAsync(project.Key, includeMerged: true, cancellationToken);
        var areas = CodeMapHeuristics.AreaFolders(project);
        var sure = new List<CodeMapHit>();
        var unsure = new List<string>();
        foreach (var path in missing)
        {
            if (Guess(path, modules, areas) is { } guess) sure.Add(guess);
            else unsure.Add(path);
        }
        if (sure.Any(h => h.Module == ModuleRegistry.CoreModule) && ModuleRegistry.Resolve(modules, ModuleRegistry.CoreModule) is null)
            await registry.EnsureAsync(project.Key, ModuleRegistry.CoreModule, "Shared plumbing: startup, configuration, layout, helpers.", cancellationToken: cancellationToken);
        if (sure.Count > 0) await SaveAsync(project.Key, sure, cancellationToken);
        hits.AddRange(sure);
        return (hits, unsure);
    }
}
