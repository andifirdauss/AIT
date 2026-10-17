using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Common.Memory;
using AiThread.Data.Memory;
using AiThread.Service.Models;
using AiThread.Service.Orchestration;
using AiThread.Service.Skills;
using AiThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Memory;

public sealed record CodeMapHit(string Path, string Module, MemoryLayer Layer, string? Area, CodeMapSource Source);

// The modules and layers of a project's files, decided once and stored (CodeMapEntry) so the same file always gets the
// same answer. Lookup order: stored entry → rules (CodeMapHeuristics) when they are sure → the model, one call per batch
// of files grouped by name stem. A file being mapped by the model is mapped once: later callers wait for that call.
public sealed class CodeMapper(LongMemoryStore store, ModuleRegistry registry, MemoryAi ai, ILogger<CodeMapper> logger)
{
    private const int GroupsPerCall = 40;
    private const int MaxTokens = 4_000;

    private readonly ConcurrentDictionary<string, Task> _inflight = new(StringComparer.OrdinalIgnoreCase);

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

    // Every file mapped: the unsure ones by the model (or, without a model, by their name stem).
    public async Task<IReadOnlyList<CodeMapHit>> MapAsync(ProjectContext project, IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        var (hits, unsure) = await QuickAsync(project, paths, cancellationToken);
        if (unsure.Count == 0) return hits;

        await MapUnsureAsync(project, unsure, cancellationToken);
        var stored = await StoredAsync(project.Key, unsure, cancellationToken);
        return hits.Concat(stored.Values.Select(ToHit)).ToList();
    }

    // Rules now, the model in the background for the rest (used before the planner, which must not wait for it).
    public async Task<IReadOnlyList<CodeMapHit>> MapQuicklyAsync(ProjectContext project, IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        var (hits, unsure) = await QuickAsync(project, paths, cancellationToken);
        if (unsure.Count > 0)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await MapUnsureAsync(project, unsure, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Background code mapping of {Count} file(s) failed.", unsure.Count);
                }
            }, CancellationToken.None);
        }
        return hits;
    }

    // A developer's (or the planner's) decision; never overwritten by rules or the model.
    public async Task<CodeMapHit> SetManualAsync(ProjectContext project, string path, string module, MemoryLayer? layer, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(project.Key)) throw new AppValidationException("Project belum punya key; buka lagi menu Project sebentar lagi.");
        var relative = Normalize(project, [path]).FirstOrDefault()
                       ?? throw new AppValidationException($"File '{path}' tidak ditemukan di project.");
        var (name, _) = await registry.EnsureAsync(project.Key, module, cancellationToken: cancellationToken);
        var stored = (await StoredAsync(project.Key, [relative], cancellationToken)).GetValueOrDefault(relative);
        var hit = new CodeMapHit(relative, name,
            layer ?? stored?.Layer ?? CodeMapHeuristics.Layer(relative) ?? MemoryLayer.Logic,
            CodeMapHeuristics.AreaOf(relative, CodeMapHeuristics.AreaFolders(project)), CodeMapSource.Manual);
        await SaveAsync(project.Key, [hit], cancellationToken, overwriteManual: true);
        return hit;
    }

    // Maps every source file of the project that has no entry yet (first registration, relabeling of old memory).
    public async Task<(int Files, int Mapped)> ScanAsync(ProjectContext project, int maxFiles, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(project.Key) || !Directory.Exists(project.RootPath)) return (0, 0);
        var scope = new WorkspaceScope(project.RootPath, ProjectLocator.IgnoredDirectoriesFor(project.Stacks));
        var files = scope.EnumerateFiles(maxFiles: maxFiles * 4)
            .Select(scope.ToRelative)
            .Where(p => p.Length <= CodeMapEntry.MaxPathLength && !scope.IsIgnoredPath(p) && CodeMapHeuristics.IsMappable(p))
            .Take(maxFiles)
            .ToList();
        var mapped = await MapAsync(project, files, cancellationToken);
        return (files.Count, mapped.Count);
    }

    // Project files a text refers to: paths that exist, and identifiers matched to file names (code map first, then the
    // files on disk).
    public async Task<IReadOnlyList<string>> ResolveReferencesAsync(ProjectContext project, string? text, CancellationToken cancellationToken)
    {
        var (paths, identifiers) = CodeMapHeuristics.References(text);
        var result = Normalize(project, paths).ToList();
        if (identifiers.Count == 0 || string.IsNullOrEmpty(project.Key)) return result;

        var wanted = identifiers.Where(i => !result.Any(p => CodeMapHeuristics.FileStem(p).Equals(i, StringComparison.OrdinalIgnoreCase))).ToList();
        if (wanted.Count == 0) return result;

        await using (var db = await store.CreateCatalogAsync(cancellationToken))
        {
            foreach (var identifier in wanted.ToList())
            {
                var marker = "/" + identifier + ".";
                var key = project.Key;
                var found = await db.CodeMapEntries.AsNoTracking()
                    .Where(e => e.ProjectKey == key && (e.Path.Contains(marker) || e.Path.StartsWith(identifier + ".")))
                    .Select(e => e.Path)
                    .Take(3)
                    .ToListAsync(cancellationToken);
                if (found.Count == 0) continue;
                result.AddRange(found);
                wanted.Remove(identifier);
            }
        }

        if (wanted.Count > 0 && Directory.Exists(project.RootPath))
        {
            var scope = new WorkspaceScope(project.RootPath, ProjectLocator.IgnoredDirectoriesFor(project.Stacks));
            var names = new HashSet<string>(wanted, StringComparer.OrdinalIgnoreCase);
            foreach (var file in scope.EnumerateFiles(maxFiles: 20_000))
            {
                if (!names.Contains(CodeMapHeuristics.FileStem(Path.GetFileName(file)))) continue;
                var relative = scope.ToRelative(file);
                if (!scope.IsIgnoredPath(relative)) result.Add(relative);
                if (result.Count >= 30) break;
            }
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Stored entries for the files and identifiers a text names, without touching the disk (the project may not be
    // cloned on this machine): used to label memory from its description.
    public async Task<IReadOnlyList<CodeMapEntry>> LookupAsync(string key, string? text, int max, CancellationToken cancellationToken)
    {
        var (paths, identifiers) = CodeMapHeuristics.References(text, 10);
        if ((paths.Count == 0 && identifiers.Count == 0) || string.IsNullOrEmpty(key)) return [];

        var result = new List<CodeMapEntry>();
        await using var db = await store.CreateCatalogAsync(cancellationToken);
        foreach (var path in paths.Select(p => p.Replace('\\', '/').TrimStart('.', '/')))
        {
            var suffix = "/" + path;
            result.AddRange(await db.CodeMapEntries.AsNoTracking()
                .Where(e => e.ProjectKey == key && (e.Path == path || e.Path.EndsWith(suffix)))
                .Take(2).ToListAsync(cancellationToken));
        }
        foreach (var identifier in identifiers)
        {
            var marker = "/" + identifier + ".";
            result.AddRange(await db.CodeMapEntries.AsNoTracking()
                .Where(e => e.ProjectKey == key && (e.Path.Contains(marker) || e.Path.StartsWith(identifier + ".")))
                .Take(2).ToListAsync(cancellationToken));
            if (result.Count >= max) break;
        }
        return result.DistinctBy(e => e.Id).Take(max).ToList();
    }

    public async Task<IReadOnlyList<CodeMapEntry>> ListAsync(string key, string? search, string? module, int take, CancellationToken cancellationToken)
    {
        await using var db = await store.CreateCatalogAsync(cancellationToken);
        var query = db.CodeMapEntries.AsNoTracking().Where(e => e.ProjectKey == key);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(e => e.Path.Contains(search.Trim()));
        if (!string.IsNullOrWhiteSpace(module)) query = query.Where(e => e.Module == module);
        return await query.OrderBy(e => e.Path).Take(take).ToListAsync(cancellationToken);
    }

    public async Task<bool> HasEntriesAsync(string key, CancellationToken cancellationToken)
    {
        await using var db = await store.CreateCatalogAsync(cancellationToken);
        return await db.CodeMapEntries.AnyAsync(e => e.ProjectKey == key, cancellationToken);
    }

    // Stored entries of the given paths, for callers that only read the map.
    public async Task<IReadOnlyDictionary<string, CodeMapEntry>> StoredAsync(string key, IReadOnlyCollection<string> paths, CancellationToken cancellationToken)
    {
        if (paths.Count == 0 || string.IsNullOrEmpty(key)) return new Dictionary<string, CodeMapEntry>();
        await using var db = await store.CreateCatalogAsync(cancellationToken);
        var result = new Dictionary<string, CodeMapEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in paths.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(200))
        {
            var rows = await db.CodeMapEntries.AsNoTracking().Where(e => e.ProjectKey == key && chunk.Contains(e.Path)).ToListAsync(cancellationToken);
            foreach (var row in rows) result[row.Path] = row;
        }
        return result;
    }

    // Files already handed to the model by another caller are awaited instead of being sent again.
    private async Task MapUnsureAsync(ProjectContext project, IReadOnlyList<string> unsure, CancellationToken cancellationToken)
    {
        var waits = new List<Task>();
        var mine = new List<string>();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        foreach (var path in unsure)
        {
            var inflightKey = project.Key + "|" + path;
            var task = _inflight.GetOrAdd(inflightKey, completion.Task);
            if (ReferenceEquals(task, completion.Task)) mine.Add(path);
            else waits.Add(task);
        }

        try
        {
            if (mine.Count > 0) await DecideAsync(project, mine, cancellationToken);
            completion.TrySetResult();
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
            throw;
        }
        finally
        {
            foreach (var path in mine) _inflight.TryRemove(project.Key + "|" + path, out _);
        }

        // Another caller's failure only means those files stay unmapped for now.
        foreach (var wait in waits)
        {
            try
            {
                await wait.WaitAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
            }
        }
    }

    private async Task DecideAsync(ProjectContext project, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        var areas = CodeMapHeuristics.AreaFolders(project);
        var groups = paths
            .GroupBy(p => CodeMapHeuristics.ModuleStem(p) ?? "(" + (Path.GetDirectoryName(p)?.Replace('\\', '/') ?? ".") + ")", StringComparer.OrdinalIgnoreCase)
            .Select((g, i) => new FileGroup($"G{i + 1}", g.Key, g.ToList()))
            .ToList();

        if (!await ai.IsAvailableAsync(cancellationToken))
        {
            await SaveFallbackAsync(project.Key, groups, areas, cancellationToken);
            return;
        }

        foreach (var batch in groups.Chunk(GroupsPerCall))
        {
            var modules = await registry.ListAsync(project.Key, cancellationToken: cancellationToken);
            JsonNode? json = null;
            try
            {
                (json, _) = await ai.AskJsonAsync(AgentPrompts.CodeMapper, Input(project, modules, batch), MaxTokens, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Code map model call failed; using name stems for {Count} group(s).", batch.Length);
            }

            var answers = (json?["groups"] as JsonArray ?? []).OfType<JsonObject>()
                .Where(o => SkillAi.Text(o["id"]) is not null)
                .GroupBy(o => SkillAi.Text(o["id"])!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var hits = new List<CodeMapHit>();
            foreach (var group in batch)
            {
                answers.TryGetValue(group.Id, out var answer);
                var moduleName = SkillAi.Text(answer?["module"]) ?? FallbackModule(group);
                var (module, _) = await registry.EnsureAsync(project.Key, moduleName, SkillAi.Text(answer?["description"]),
                    MemoryAi.Strings(answer?["aliases"]), cancellationToken);
                var layers = answer?["layers"] as JsonObject;
                foreach (var path in group.Paths)
                {
                    var layer = CodeMapHeuristics.Layer(path)
                                ?? (MemoryLabels.TryParseLayer(SkillAi.Text(layers?[path]), out var byModel) ? byModel : MemoryLayer.Logic);
                    hits.Add(new CodeMapHit(path, module, layer, CodeMapHeuristics.AreaOf(path, areas), answer is null ? CodeMapSource.Heuristic : CodeMapSource.AI));
                }
            }
            await SaveAsync(project.Key, hits, cancellationToken);
        }
    }

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

    private static string FallbackModule(FileGroup group) =>
        group.Stem.StartsWith('(') ? ModuleRegistry.CoreModule : group.Stem;

    // Sure when the layer is known and the name stem is an existing module (or the file is plumbing that belongs to Core).
    private static CodeMapHit? Guess(string path, IReadOnlyList<ProjectModule> modules, IReadOnlyList<string> areas)
    {
        var layer = CodeMapHeuristics.Layer(path);
        if (layer is null) return null;
        var stem = CodeMapHeuristics.ModuleStem(path);
        var module = stem is null ? null : ModuleRegistry.Resolve(modules, stem);
        if (module is null && layer is MemoryLayer.Infra) module = ModuleRegistry.Resolve(modules, ModuleRegistry.CoreModule) ?? ModuleRegistry.CoreModule;
        return module is null ? null : new CodeMapHit(path, module, layer.Value, CodeMapHeuristics.AreaOf(path, areas), CodeMapSource.Heuristic);
    }

    private static string Input(ProjectContext project, IReadOnlyList<ProjectModule> modules, IEnumerable<FileGroup> groups)
    {
        var builder = new StringBuilder();
        builder.Append("# Project\n").Append(project.Name).Append("\n\n# Modules\n");
        if (modules.Count == 0) builder.Append("(none yet)\n");
        foreach (var module in modules)
        {
            builder.Append("- ").Append(module.Name);
            if (!string.IsNullOrEmpty(module.Aliases)) builder.Append(" (aliases: ").Append(module.Aliases.Replace(",", ", ")).Append(')');
            if (!string.IsNullOrEmpty(module.Description)) builder.Append(": ").Append(module.Description);
            builder.Append('\n');
        }
        builder.Append("\n# Groups\n");
        foreach (var group in groups)
        {
            builder.Append("## ").Append(group.Id).Append(" stem ").Append(group.Stem).Append('\n');
            foreach (var path in group.Paths)
                builder.Append(path).Append(" [").Append(CodeMapHeuristics.Layer(path)?.ToString() ?? "?").Append("]\n");
        }
        return builder.ToString();
    }

    private async Task SaveAsync(string key, IReadOnlyList<CodeMapHit> hits, CancellationToken cancellationToken, bool overwriteManual = false)
    {
        if (hits.Count == 0) return;
        await using var db = await store.CreateCatalogAsync(cancellationToken);
        foreach (var chunk in hits.DistinctBy(h => h.Path, StringComparer.OrdinalIgnoreCase).Chunk(200))
        {
            var paths = chunk.Select(h => h.Path).ToList();
            var existing = await db.CodeMapEntries.Where(e => e.ProjectKey == key && paths.Contains(e.Path))
                .ToDictionaryAsync(e => e.Path, StringComparer.OrdinalIgnoreCase, cancellationToken);
            var now = DateTime.UtcNow;
            foreach (var hit in chunk)
            {
                if (existing.TryGetValue(hit.Path, out var row))
                {
                    if (row.Source == CodeMapSource.Manual && !overwriteManual) continue;
                    row.Module = hit.Module;
                    row.Layer = hit.Layer;
                    row.Area = hit.Area;
                    row.Source = hit.Source;
                    row.UpdatedAt = now;
                }
                else
                {
                    db.CodeMapEntries.Add(new CodeMapEntry
                    {
                        Id = Guid.NewGuid(), ProjectKey = key, Path = hit.Path, Module = hit.Module, Layer = hit.Layer,
                        Area = hit.Area, Source = hit.Source, CreatedAt = now
                    });
                }
            }
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                // Another developer's AiThread mapped the same file meanwhile; their entry stands.
                logger.LogDebug(ex, "Code map entries of {Key} were written concurrently.", key);
            }
        }
    }

    // Relative paths inside the project, forward slashes, existing or not (a task may have deleted the file).
    private static List<string> Normalize(ProjectContext project, IEnumerable<string> paths)
    {
        var scope = new WorkspaceScope(project.RootPath);
        var result = new List<string>();
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !scope.TryResolve(path, out var full, out _)) continue;
            var relative = scope.ToRelative(full);
            if (relative is "." or "" || relative.Length > CodeMapEntry.MaxPathLength || scope.IsIgnoredPath(relative)) continue;
            if (!result.Contains(relative, StringComparer.OrdinalIgnoreCase)) result.Add(relative);
        }
        return result;
    }

    private static CodeMapHit ToHit(CodeMapEntry e) => new(e.Path, e.Module, e.Layer, e.Area, e.Source);

    private sealed record FileGroup(string Id, string Stem, List<string> Paths);
}
