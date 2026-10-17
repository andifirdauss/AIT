using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Common.Memory;
using AiThread.Data.Cache;
using AiThread.Data.Memory;
using AiThread.Data.Messaging;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Orchestration;
using AiThread.Service.Skills;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Memory;

// What a merge rewrote, so an undo restores exactly those rows.
public sealed record ModuleMergeMoves(
    List<Guid> LongMemories,
    List<Guid> ShortMemories,
    List<Guid> CodeMap,
    List<Guid> Pages,
    List<string> AddedAliases,
    List<string> SkippedPages);

// Folds a module into another (memories, code map, business pages and the registry follow) and finds modules that
// were split by accident: same name written differently is merged right away, the model judges the rest. A judgement
// at or above AutoMergeConfidence is applied (and can be undone); a lower one waits in Memory → Perlu review.
public sealed class ModuleMerger(
    LongMemoryStore longStore,
    IShortMemoryStore shortStore,
    BusinessMemoryStore businessStore,
    ModuleRegistry registry,
    MemoryAi ai,
    IEventBus eventBus,
    IActivityLogService activityLog,
    ILogger<ModuleMerger> logger)
{
    public const double AutoMergeConfidence = 0.85;
    public const string AutoDecider = "Auto";

    private const int MaxTokens = 2_000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ModuleMergeLog> MergeAsync(string key, string from, string into, string? reason, double? confidence, string decidedBy,
        CancellationToken cancellationToken, Guid? suggestionId = null)
    {
        var modules = await registry.ListAsync(key, includeMerged: true, cancellationToken);
        var source = modules.FirstOrDefault(m => m.MergedInto is null && string.Equals(m.Name, from, StringComparison.OrdinalIgnoreCase))
                     ?? throw new AppValidationException($"Modul '{from}' tidak ditemukan.");
        var targetName = ModuleRegistry.Resolve(modules, into) ?? throw new AppValidationException($"Modul '{into}' tidak ditemukan.");
        if (string.Equals(source.Name, targetName, StringComparison.OrdinalIgnoreCase))
            throw new AppValidationException("Modul asal dan tujuan sama.");

        var moves = new ModuleMergeMoves([], [], [], [], [], []);
        var knowledge = key == MemoryLabels.KnowledgeKey;

        // Registry: the source becomes a redirect, its names aliases of the target.
        await using (var catalog = await longStore.CreateCatalogAsync(cancellationToken))
        {
            var sourceRow = await catalog.ProjectModules.FirstAsync(m => m.Id == source.Id, cancellationToken);
            var targetRow = await catalog.ProjectModules.FirstAsync(m => m.ProjectKey == key && m.Name == targetName, cancellationToken);
            var before = MemoryLabels.SplitCsv(targetRow.Aliases).ToHashSet(StringComparer.OrdinalIgnoreCase);
            ModuleRegistry.MergeAliases(targetRow, MemoryLabels.SplitCsv(sourceRow.Aliases).Prepend(sourceRow.Name));
            moves.AddedAliases.AddRange(MemoryLabels.SplitCsv(targetRow.Aliases).Where(a => !before.Contains(a)));
            targetRow.UpdatedAt = DateTime.UtcNow;
            if (string.IsNullOrEmpty(targetRow.Description)) targetRow.Description = sourceRow.Description;
            sourceRow.MergedInto = targetRow.Name;
            sourceRow.UpdatedAt = DateTime.UtcNow;

            var sourceName = sourceRow.Name;
            var mapRows = await catalog.CodeMapEntries.Where(e => e.ProjectKey == key && e.Module == sourceName).ToListAsync(cancellationToken);
            foreach (var row in mapRows)
            {
                row.Module = targetRow.Name;
                row.UpdatedAt = DateTime.UtcNow;
                moves.CodeMap.Add(row.Id);
            }
            await catalog.SaveChangesAsync(cancellationToken);
        }

        await MoveLongAsync(key, knowledge, source.Name, targetName, null, moves.LongMemories, cancellationToken);
        await MoveShortAsync(key, knowledge, source.Name, targetName, null, moves.ShortMemories);
        if (!knowledge) await MovePagesAsync(key, source.Name, targetName, moves, cancellationToken);

        ModuleMergeLog log;
        await using (var catalog = await longStore.CreateCatalogAsync(cancellationToken))
        {
            log = suggestionId is { } id ? await catalog.ModuleMergeLogs.FirstOrDefaultAsync(l => l.Id == id, cancellationToken) ?? NewLog() : NewLog();
            if (log.Id != suggestionId) catalog.ModuleMergeLogs.Add(log);
            log.FromModule = source.Name;
            log.IntoModule = targetName;
            log.Status = ModuleMergeStatus.Applied;
            log.Reason = Clip(reason ?? log.Reason, 1000);
            log.Confidence = confidence ?? log.Confidence;
            log.DecidedBy = Clip(decidedBy, 200);
            log.DecidedAt = DateTime.UtcNow;
            log.Moved = JsonSerializer.Serialize(moves, Json);
            await catalog.SaveChangesAsync(cancellationToken);
        }

        var note = moves.SkippedPages.Count > 0 ? $" {moves.SkippedPages.Count} halaman wiki tidak dipindah karena judulnya sudah ada di {targetName}." : string.Empty;
        activityLog.Info("memory", $"Modul {source.Name} digabung ke {targetName} ({decidedBy}): {moves.LongMemories.Count + moves.ShortMemories.Count} memory, " +
                                   $"{moves.CodeMap.Count} file, {moves.Pages.Count} halaman wiki.{note}");
        Publish("merged", key);
        return log;

        ModuleMergeLog NewLog() => new() { Id = Guid.NewGuid(), ProjectKey = key, CreatedAt = DateTime.UtcNow };
    }

    public async Task UndoAsync(Guid logId, string decidedBy, CancellationToken cancellationToken)
    {
        ModuleMergeLog log;
        ModuleMergeMoves moves;
        await using (var catalog = await longStore.CreateCatalogAsync(cancellationToken))
        {
            log = await catalog.ModuleMergeLogs.FirstOrDefaultAsync(l => l.Id == logId, cancellationToken)
                  ?? throw new AppValidationException("Riwayat merge tidak ditemukan.");
            if (log.Status != ModuleMergeStatus.Applied) throw new AppValidationException("Hanya merge yang sudah diterapkan yang bisa dibatalkan.");
            moves = JsonSerializer.Deserialize<ModuleMergeMoves>(log.Moved ?? "{}", Json) ?? new ModuleMergeMoves([], [], [], [], [], []);

            var key = log.ProjectKey;
            var from = log.FromModule;
            var into = log.IntoModule;
            var source = await catalog.ProjectModules.FirstOrDefaultAsync(m => m.ProjectKey == key && m.Name == from, cancellationToken);
            if (source is not null)
            {
                source.MergedInto = null;
                source.UpdatedAt = DateTime.UtcNow;
            }
            var target = await catalog.ProjectModules.FirstOrDefaultAsync(m => m.ProjectKey == key && m.Name == into, cancellationToken);
            if (target is not null && moves.AddedAliases.Count > 0)
            {
                var removed = moves.AddedAliases.ToHashSet(StringComparer.OrdinalIgnoreCase);
                target.Aliases = MemoryLabels.JoinCsv(MemoryLabels.SplitCsv(target.Aliases).Where(a => !removed.Contains(a)));
                target.UpdatedAt = DateTime.UtcNow;
            }

            var mapIds = moves.CodeMap;
            var mapRows = await catalog.CodeMapEntries.Where(e => mapIds.Contains(e.Id) && e.Module == into).ToListAsync(cancellationToken);
            foreach (var row in mapRows)
            {
                row.Module = from;
                row.UpdatedAt = DateTime.UtcNow;
            }

            log.Status = ModuleMergeStatus.Undone;
            log.DecidedBy = Clip(decidedBy, 200);
            log.DecidedAt = DateTime.UtcNow;
            await catalog.SaveChangesAsync(cancellationToken);
        }

        var knowledge = log.ProjectKey == MemoryLabels.KnowledgeKey;
        await MoveLongAsync(log.ProjectKey, knowledge, log.IntoModule, log.FromModule, moves.LongMemories.ToHashSet(), [], cancellationToken);
        await MoveShortAsync(log.ProjectKey, knowledge, log.IntoModule, log.FromModule, moves.ShortMemories.ToHashSet(), []);
        if (moves.Pages.Count > 0)
        {
            await using var business = await businessStore.CreateAsync(cancellationToken);
            var ids = moves.Pages;
            var into = log.IntoModule;
            var from = log.FromModule;
            await business.BusinessPages.Where(p => ids.Contains(p.Id) && p.Module == into)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Module, from), cancellationToken);
        }

        activityLog.Info("memory", $"Merge modul {log.FromModule} → {log.IntoModule} dibatalkan oleh {decidedBy}.");
        Publish("unmerged", log.ProjectKey);
    }

    public async Task RejectAsync(Guid logId, string decidedBy, CancellationToken cancellationToken)
    {
        await using var catalog = await longStore.CreateCatalogAsync(cancellationToken);
        var log = await catalog.ModuleMergeLogs.FirstOrDefaultAsync(l => l.Id == logId, cancellationToken)
                  ?? throw new AppValidationException("Saran merge tidak ditemukan.");
        if (log.Status != ModuleMergeStatus.Suggested) throw new AppValidationException("Saran ini sudah diputuskan.");
        log.Status = ModuleMergeStatus.Rejected;
        log.DecidedBy = Clip(decidedBy, 200);
        log.DecidedAt = DateTime.UtcNow;
        await catalog.SaveChangesAsync(cancellationToken);
        Publish("rejected", log.ProjectKey);
    }

    public async Task AcceptAsync(Guid logId, string decidedBy, CancellationToken cancellationToken)
    {
        ModuleMergeLog log;
        await using (var catalog = await longStore.CreateCatalogAsync(cancellationToken))
            log = await catalog.ModuleMergeLogs.AsNoTracking().FirstOrDefaultAsync(l => l.Id == logId, cancellationToken)
                  ?? throw new AppValidationException("Saran merge tidak ditemukan.");
        if (log.Status != ModuleMergeStatus.Suggested) throw new AppValidationException("Saran ini sudah diputuskan.");
        await MergeAsync(log.ProjectKey, log.FromModule, log.IntoModule, log.Reason, log.Confidence, decidedBy, cancellationToken, log.Id);
    }

    // Looks for duplicates of the given modules (all modules when null). Returns merges applied plus suggestions made.
    public async Task<int> CheckAsync(string key, IReadOnlyCollection<string>? focus, CancellationToken cancellationToken)
    {
        var count = 0;

        // 1. Same name written differently (case, separators, plural): merged right away.
        var modules = (await registry.ListAsync(key, cancellationToken: cancellationToken)).ToList();
        var usage = await UsageAsync(key, cancellationToken);
        foreach (var (a, b) in Pairs(modules, focus).Where(p => MemoryLabels.SameName(p.A.Name, p.B.Name)).ToList())
        {
            var (from, into) = Keep(a, b, usage);
            try
            {
                await MergeAsync(key, from.Name, into.Name, "Nama sama dengan penulisan berbeda.", 1.0, AutoDecider, cancellationToken);
                count++;
            }
            catch (AppValidationException)
            {
                // Already folded by an earlier pair.
            }
        }

        // 2. The model judges the rest (translations, longer names, typos).
        modules = (await registry.ListAsync(key, cancellationToken: cancellationToken)).ToList();
        if (modules.Count < 2 || !await ai.IsAvailableAsync(cancellationToken)) return count;
        var focusNames = focus?.Select(f => ModuleRegistry.Resolve(modules, f)).OfType<string>().Distinct().ToList();
        if (focusNames is { Count: 0 }) return count;

        JsonNode? json;
        try
        {
            (json, _) = await ai.AskJsonAsync(AgentPrompts.ModuleDuplicates, await InputAsync(key, modules, focusNames, cancellationToken), MaxTokens, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Duplicate module check of {Key} failed.", key);
            return count;
        }

        var decided = await DecidedPairsAsync(key, cancellationToken);
        foreach (var item in (json?["merges"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var current = await registry.ListAsync(key, includeMerged: true, cancellationToken);
            var from = ModuleRegistry.Resolve(current, SkillAi.Text(item["from"]));
            var into = ModuleRegistry.Resolve(current, SkillAi.Text(item["into"]));
            if (from is null || into is null || string.Equals(from, into, StringComparison.OrdinalIgnoreCase)) continue;
            if (decided.Contains(PairKey(from, into))) continue;

            var confidence = Math.Clamp(SkillAi.Number(item["confidence"]) ?? 0, 0, 1);
            var reason = SkillAi.Text(item["reason"]);
            if (confidence >= AutoMergeConfidence)
            {
                await MergeAsync(key, from, into, reason, confidence, AutoDecider, cancellationToken);
            }
            else
            {
                await using var catalog = await longStore.CreateCatalogAsync(cancellationToken);
                catalog.ModuleMergeLogs.Add(new ModuleMergeLog
                {
                    Id = Guid.NewGuid(), ProjectKey = key, FromModule = from, IntoModule = into, Status = ModuleMergeStatus.Suggested,
                    Reason = Clip(reason, 1000), Confidence = confidence, CreatedAt = DateTime.UtcNow
                });
                await catalog.SaveChangesAsync(cancellationToken);
                activityLog.Info("memory", $"Saran merge modul {from} → {into} (keyakinan {confidence:P0}) menunggu review di menu Memory.");
                Publish("suggested", key);
            }
            decided.Add(PairKey(from, into));
            count++;
        }
        return count;
    }

    public async Task<IReadOnlyList<ModuleMergeDto>> ListAsync(string key, ModuleMergeStatus? status, int take, CancellationToken cancellationToken)
    {
        await using var catalog = await longStore.CreateCatalogAsync(cancellationToken);
        var query = catalog.ModuleMergeLogs.AsNoTracking().Where(l => l.ProjectKey == key);
        if (status is { } s) query = query.Where(l => l.Status == s);
        var rows = await query.OrderByDescending(l => l.CreatedAt).Take(take).ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    public static ModuleMergeDto ToDto(ModuleMergeLog log)
    {
        var moves = string.IsNullOrEmpty(log.Moved) ? null : JsonSerializer.Deserialize<ModuleMergeMoves>(log.Moved, Json);
        return new ModuleMergeDto(log.Id, log.FromModule, log.IntoModule, log.Status, log.Reason, log.Confidence, log.DecidedBy, log.CreatedAt,
            log.DecidedAt, (moves?.LongMemories.Count ?? 0) + (moves?.ShortMemories.Count ?? 0), moves?.CodeMap.Count ?? 0, moves?.Pages.Count ?? 0);
    }

    // restrictTo: only these rows (an undo); moved collects the ids that changed.
    private async Task MoveLongAsync(string key, bool knowledge, string from, string into, HashSet<Guid>? restrictTo, List<Guid> moved, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await longStore.CreateAsync(cancellationToken);
            var query = knowledge
                ? db.LongMemories.Where(m => m.Type == MemoryType.Knowledge && m.Module == from)
                : db.LongMemories.Where(m => m.ProjectKey == key && m.Type != MemoryType.Knowledge && m.Module == from);
            var rows = await query.ToListAsync(cancellationToken);
            foreach (var row in rows.Where(r => restrictTo is null || restrictTo.Contains(r.Id)))
            {
                row.Module = into;
                if (row.Kind is { } kind)
                    row.Label = MemoryLabels.Compose(MemoryLabels.Display(row.Area, into, MemoryLabels.ParseLayers(row.Layers), kind), MemoryLabels.SplitCsv(row.Keywords));
                row.UpdatedAt = DateTime.UtcNow;
                moved.Add(row.Id);
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogStoreWarning(ex, $"Could not move long memories from module {from} to {into}");
        }
    }

    private async Task MoveShortAsync(string key, bool knowledge, string from, string into, HashSet<Guid>? restrictTo, List<Guid> moved)
    {
        if (!shortStore.IsConnected) return;
        try
        {
            var records = (await shortStore.GetAllAsync())
                .Where(r => r.Module == from && (knowledge ? r.Type == MemoryType.Knowledge : r.ProjectKey == key && r.Type != MemoryType.Knowledge))
                .Where(r => restrictTo is null || restrictTo.Contains(r.Id))
                .ToList();
            foreach (var record in records)
            {
                var label = record.Kind is { } kind
                    ? MemoryLabels.Compose(MemoryLabels.Display(record.Area, into, MemoryLabels.ParseLayers(record.Layers), kind), MemoryLabels.SplitCsv(record.Keywords))
                    : record.Label;
                await shortStore.SaveAsync(record with { Module = into, Label = label });
                moved.Add(record.Id);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not move short memories from module {From} to {Into}.", from, into);
        }
    }

    // A page whose title already exists in the target module stays where it is (no duplicate page); the wiki pages
    // are never merged automatically.
    private async Task MovePagesAsync(string key, string from, string into, ModuleMergeMoves moves, CancellationToken cancellationToken)
    {
        try
        {
            await using var business = await businessStore.CreateAsync(cancellationToken);
            var pages = await business.BusinessPages.AsNoTracking().Where(p => p.ProjectKey == key && (p.Module == from || p.Module == into))
                .Select(p => new { p.Id, p.Module, p.Title }).ToListAsync(cancellationToken);
            var takenTitles = pages.Where(p => p.Module == into).Select(p => p.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var page in pages.Where(p => p.Module == from))
            {
                if (takenTitles.Contains(page.Title))
                {
                    moves.SkippedPages.Add(page.Title);
                    continue;
                }
                moves.Pages.Add(page.Id);
            }
            if (moves.Pages.Count == 0) return;
            var ids = moves.Pages;
            await business.BusinessPages.Where(p => ids.Contains(p.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Module, into), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not move business pages from module {From} to {Into}.", from, into);
        }
    }

    // The module to keep: the more used one, then the shorter (singular) name.
    private static (ProjectModule From, ProjectModule Into) Keep(ProjectModule a, ProjectModule b, IReadOnlyDictionary<string, int> usage)
    {
        var ua = usage.GetValueOrDefault(a.Name);
        var ub = usage.GetValueOrDefault(b.Name);
        if (ua != ub) return ua > ub ? (b, a) : (a, b);
        return a.Name.Length <= b.Name.Length ? (b, a) : (a, b);
    }

    private static IEnumerable<(ProjectModule A, ProjectModule B)> Pairs(IReadOnlyList<ProjectModule> modules, IReadOnlyCollection<string>? focus)
    {
        for (var i = 0; i < modules.Count; i++)
        for (var j = i + 1; j < modules.Count; j++)
        {
            if (focus is not null && !focus.Contains(modules[i].Name, StringComparer.OrdinalIgnoreCase)
                                  && !focus.Contains(modules[j].Name, StringComparer.OrdinalIgnoreCase)) continue;
            yield return (modules[i], modules[j]);
        }
    }

    // Memories plus mapped files per module.
    private async Task<Dictionary<string, int>> UsageAsync(string key, CancellationToken cancellationToken)
    {
        var usage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using (var catalog = await longStore.CreateCatalogAsync(cancellationToken))
            {
                var files = await catalog.CodeMapEntries.AsNoTracking().Where(e => e.ProjectKey == key)
                    .GroupBy(e => e.Module).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
                foreach (var f in files) usage[f.Key] = f.Count;
            }
            await using var db = await longStore.CreateAsync(cancellationToken);
            var knowledge = key == MemoryLabels.KnowledgeKey;
            var memories = await (knowledge ? db.LongMemories.Where(m => m.Type == MemoryType.Knowledge) : db.LongMemories.Where(m => m.ProjectKey == key))
                .Where(m => m.Module != null).GroupBy(m => m.Module!).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
            foreach (var m in memories) usage[m.Key] = usage.GetValueOrDefault(m.Key) + m.Count;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogStoreWarning(ex, "Could not count module usage");
        }
        return usage;
    }

    private async Task<string> InputAsync(string key, IReadOnlyList<ProjectModule> modules, IReadOnlyList<string>? focus, CancellationToken cancellationToken)
    {
        var files = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var labels = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using (var catalog = await longStore.CreateCatalogAsync(cancellationToken))
            {
                var rows = await catalog.CodeMapEntries.AsNoTracking().Where(e => e.ProjectKey == key)
                    .OrderBy(e => e.Path).Select(e => new { e.Module, e.Path }).Take(3000).ToListAsync(cancellationToken);
                foreach (var group in rows.GroupBy(r => r.Module)) files[group.Key] = group.Take(4).Select(r => r.Path).ToList();
            }
            await using var db = await longStore.CreateAsync(cancellationToken);
            var knowledge = key == MemoryLabels.KnowledgeKey;
            var memories = await (knowledge ? db.LongMemories.Where(m => m.Type == MemoryType.Knowledge) : db.LongMemories.Where(m => m.ProjectKey == key))
                .Where(m => m.Module != null).OrderByDescending(m => m.HitCount).Select(m => new { m.Module, m.Label }).Take(1000).ToListAsync(cancellationToken);
            foreach (var group in memories.GroupBy(m => m.Module!)) labels[group.Key] = group.Take(3).Select(m => m.Label).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogStoreWarning(ex, "Could not read module samples");
        }

        var builder = new StringBuilder();
        builder.Append("# Check\n").Append(focus is null ? "all" : string.Join(", ", focus)).Append("\n\n# Modules\n");
        foreach (var module in modules)
        {
            builder.Append("## ").Append(module.Name).Append('\n');
            if (!string.IsNullOrEmpty(module.Aliases)) builder.Append("aliases: ").Append(module.Aliases.Replace(",", ", ")).Append('\n');
            if (!string.IsNullOrEmpty(module.Description)) builder.Append("description: ").Append(module.Description).Append('\n');
            if (files.TryGetValue(module.Name, out var paths)) builder.Append("files: ").Append(string.Join(", ", paths)).Append('\n');
            if (labels.TryGetValue(module.Name, out var memoryLabels)) builder.Append("memories: ").Append(string.Join(" / ", memoryLabels)).Append('\n');
        }
        return builder.ToString();
    }

    // Pairs already decided (rejected, undone, or still suggested) are not suggested again.
    private async Task<HashSet<string>> DecidedPairsAsync(string key, CancellationToken cancellationToken)
    {
        await using var catalog = await longStore.CreateCatalogAsync(cancellationToken);
        var rows = await catalog.ModuleMergeLogs.AsNoTracking()
            .Where(l => l.ProjectKey == key && l.Status != ModuleMergeStatus.Applied)
            .Select(l => new { l.FromModule, l.IntoModule }).ToListAsync(cancellationToken);
        return rows.Select(r => PairKey(r.FromModule, r.IntoModule)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string PairKey(string a, string b) =>
        string.Compare(a, b, StringComparison.OrdinalIgnoreCase) < 0 ? $"{a}|{b}" : $"{b}|{a}";

    private void Publish(string action, string key) =>
        eventBus.Publish(AppEvent.Create(AppEventTypes.MemoryChanged, new { id = (Guid?)null, action, projectKey = key }));

    private static string? Clip(string? value, int max) =>
        string.IsNullOrEmpty(value) ? null : value.Length > max ? value[..max] : value;
}
