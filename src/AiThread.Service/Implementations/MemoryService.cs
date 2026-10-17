using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Common.Memory;
using AiThread.Common.Options;
using AiThread.Data.Cache;
using AiThread.Data.Context;
using AiThread.Data.Memory;
using AiThread.Data.Messaging;
using AiThread.Service.Business;
using AiThread.Service.Interfaces;
using AiThread.Service.Memory;
using AiThread.Service.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiThread.Service.Implementations;

// Project memory is scoped by Project.ProjectKey, so every clone of a repository (on any developer's machine, with
// any root folder) reads and writes the same memory. Rows written before keys existed carry only ProjectId and still
// match their own project. The project's business wiki pages are part of the planner's index too (MemoryType.Business),
// read-only here: they are written by the business wiki writer and in the Memory menu.
// Every memory is written with its structured label (MemoryLabels); memory from older versions stays readable as
// "unlabeled" until MemoryRelabelWorker relabels it.
public sealed class MemoryService(
    IDbContextFactory<AiThreadDbContext> dbFactory,
    SwitchableShortMemoryStore shortStore,
    LongMemoryStore longStore,
    BusinessMemoryStore businessStore,
    ModuleRegistry registry,
    IEventBus eventBus,
    IActivityLogService activityLog,
    IOptions<AiThreadOptions> options,
    ILogger<MemoryService> logger) : IMemoryService
{
    private const int PlannerPageMaxChars = 15_000;

    // While the long memory database is down the planner keeps running without it; the activity log says so at most
    // this often instead of once per thread.
    private static readonly TimeSpan LongStoreNoticeInterval = TimeSpan.FromMinutes(10);
    private long _longStoreNoticeTicks;

    public async Task<MemoryItemDto> AddShortAsync(SaveMemoryRequest request, Guid? sourceThreadId = null, CancellationToken cancellationToken = default)
    {
        var projects = await LocalProjectsAsync(cancellationToken);
        var owner = request.Type == MemoryType.Knowledge ? null : projects.Find(request.ProjectId);
        var label = await PrepareLabelAsync(request, owner, cancellationToken);
        var record = new ShortMemoryRecord(
            Guid.NewGuid(),
            request.Type == MemoryType.Knowledge ? null : request.ProjectId,
            request.Type,
            label.Composed,
            request.Description.Trim(),
            sourceThreadId,
            DateTime.UtcNow,
            owner?.Key,
            owner?.Name,
            label.Module,
            MemoryLabels.LayersCsv(label.Layers),
            label.Kind,
            label.Area,
            MemoryLabels.JoinCsv(label.Keywords),
            MemoryLabels.CurrentVersion,
            ExtraModules: MemoryLabels.JoinCsv(label.ExtraModules));

        if (shortStore.IsConnected)
        {
            try
            {
                await shortStore.SaveAsync(record);
                Publish("created", record.Id);
                return ToDto(record, projects);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Short memory write failed; storing memory directly as long memory.");
            }
        }

        // Short memory store unavailable: keep the memory rather than losing it.
        var entity = await InsertLongAsync(record, cancellationToken);
        activityLog.Warn("memory", $"Penyimpanan short memory tidak tersedia — memory '{record.Label}' langsung disimpan sebagai long memory.", sourceThreadId, record.ProjectId);
        Publish("created", entity.Id);
        return ToDto(entity, projects);
    }

    public Task<IReadOnlyList<MemoryIndexEntry>> GetIndexAsync(Guid? projectId, string? query, int take, CancellationToken cancellationToken = default) =>
        SearchAsync(new MemorySearch(projectId, Text: query, Take: take), cancellationToken);

    public async Task<IReadOnlyList<MemoryIndexEntry>> SearchAsync(MemorySearch search, CancellationToken cancellationToken = default)
    {
        var take = Math.Max(1, search.Take);
        var scope = (await LocalProjectsAsync(cancellationToken)).Find(search.ProjectId);

        var entries = (await SafeShortAsync())
            .Where(r => r.Type == MemoryType.Knowledge || (scope is not null && scope.Owns(r.ProjectKey, r.ProjectId)))
            .Select(ToIndexEntry)
            .ToList();
        entries.AddRange(await LongIndexAsync(scope, cancellationToken));

        var tokens = MemoryLabels.Tokens(string.Join(' ', (search.Keywords ?? []).Append(search.Text ?? string.Empty)));
        var modules = await WantedModulesAsync(scope, search, tokens, cancellationToken);

        // Business pages and unlabeled memory get their own share, so neither crowds the labeled memory out.
        var pages = MemoryRanker.RankPages(await BusinessIndexAsync(scope, cancellationToken), modules, tokens, Math.Min(8, Math.Max(1, take / 3)));
        var unlabeledShare = entries.Any(e => !e.Labeled) ? Math.Max(3, take / 4) : 0;
        var labeled = MemoryRanker.RankLabeled(entries, modules, search.Layer, search.Kind, tokens, take - pages.Count - Math.Min(unlabeledShare, 3));
        var unlabeled = MemoryRanker.RankUnlabeled(entries, tokens, Math.Min(unlabeledShare, take - pages.Count - labeled.Count));
        return pages.Concat(labeled).Concat(unlabeled).Take(take).ToList();
    }

    public async Task<IReadOnlyList<MemoryModuleSummary>> GetModuleSummaryAsync(Guid? projectId, CancellationToken cancellationToken = default)
    {
        var scope = (await LocalProjectsAsync(cancellationToken)).Find(projectId);
        var entries = (await SafeShortAsync())
            .Where(r => r.Type == MemoryType.Knowledge || (scope is not null && scope.Owns(r.ProjectKey, r.ProjectId)))
            .Select(ToIndexEntry)
            .Concat(await LongIndexAsync(scope, cancellationToken))
            .Where(e => e.Labeled)
            .ToList();

        // A memory of several modules counts for each of them.
        return entries
            .SelectMany(e => e.Modules.Select(module => (Module: module, Entry: e)))
            .GroupBy(x => (x.Module, x.Entry.Type == MemoryType.Knowledge), x => x.Entry)
            .Select(g => new MemoryModuleSummary(
                g.Key.Item1,
                g.Key.Item2,
                g.Count(),
                g.SelectMany(e => e.Layers ?? []).GroupBy(l => l).ToDictionary(x => x.Key, x => x.Count()),
                g.GroupBy(e => e.Kind!.Value).ToDictionary(x => x.Key, x => x.Count())))
            .OrderBy(s => s.Knowledge)
            .ThenByDescending(s => s.Count)
            .ThenBy(s => s.Module)
            .ToList();
    }

    // Explicit modules (resolved against the registry), else the modules the text names.
    private async Task<IReadOnlyCollection<string>?> WantedModulesAsync(ProjectScope? scope, MemorySearch search, IReadOnlyList<string> tokens, CancellationToken cancellationToken)
    {
        var registryModules = new List<ProjectModule>();
        try
        {
            if (scope?.Key is { } key) registryModules.AddRange(await registry.ListAsync(key, includeMerged: true, cancellationToken));
            registryModules.AddRange(await registry.ListAsync(MemoryLabels.KnowledgeKey, includeMerged: true, cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LongStoreFailed(ex, "Could not read the module list");
        }

        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in (search.Modules ?? []).Append(search.Module).Where(n => !string.IsNullOrWhiteSpace(n)))
            wanted.Add(ModuleRegistry.Resolve(registryModules, name) ?? MemoryLabels.Module(name)!);
        if (wanted.Count == 0) wanted.UnionWith(MemoryRanker.DetectModules(registryModules, tokens));
        return wanted.Count == 0 ? null : wanted;
    }

    // Long memory may live in an external database too; an outage there only hides those memories, the thread still runs.
    private async Task<IReadOnlyList<MemoryIndexEntry>> LongIndexAsync(ProjectScope? scope, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await longStore.CreateAsync(cancellationToken);
            var rows = await WhereVisible(db.LongMemories.AsNoTracking(), scope, includeKnowledge: true)
                .Select(m => new
                {
                    m.Id, m.Type, m.Label, m.CreatedAt, m.Module, m.ExtraModules, m.Layers, m.Kind, m.Area, m.Keywords, m.HitCount, m.LabelVersion
                })
                .ToListAsync(cancellationToken);
            return rows.Select(m => m.LabelVersion is null || m.Module is null || m.Kind is null
                ? new MemoryIndexEntry(m.Id, MemoryLevel.Long, m.Type, m.Label, m.CreatedAt, HitCount: m.HitCount)
                : new MemoryIndexEntry(m.Id, MemoryLevel.Long, m.Type, m.Label, m.CreatedAt, m.Module, MemoryLabels.ParseLayers(m.Layers), m.Kind,
                    m.Area, MemoryLabels.SplitCsv(m.Keywords), m.HitCount, MemoryLabels.SplitCsv(m.ExtraModules))).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LongStoreFailed(ex, "Could not read the long memory index");
            return [];
        }
    }

    private void LongStoreFailed(Exception ex, string what)
    {
        logger.LogStoreWarning(ex, what);
        if (!StoreErrors.IsUnreachable(ex)) return;

        var now = DateTime.UtcNow.Ticks;
        var last = Interlocked.Read(ref _longStoreNoticeTicks);
        if (now - last < LongStoreNoticeInterval.Ticks || Interlocked.CompareExchange(ref _longStoreNoticeTicks, now, last) != last) return;
        activityLog.Warn("memory", $"Long memory ({longStore.Description}) tidak dapat dihubungi: {MemoryStoreTester.ErrorMessage(ex)}. Thread tetap berjalan tanpa long memory.");
    }

    // The business wiki may live in another (shared) database; an outage there only hides the pages.
    private async Task<IReadOnlyList<MemoryIndexEntry>> BusinessIndexAsync(ProjectScope? scope, CancellationToken cancellationToken)
    {
        if (scope?.Key is not { } key) return [];
        try
        {
            await using var db = await businessStore.CreateAsync(cancellationToken);
            var rows = await db.BusinessPages.AsNoTracking()
                .Where(p => p.ProjectKey == key)
                .Select(p => new { p.Id, p.Module, p.Title, p.Labels, p.CreatedAt, p.UpdatedAt })
                .ToListAsync(cancellationToken);
            return rows.Select(p => new MemoryIndexEntry(p.Id, MemoryLevel.Business, MemoryType.Business,
                $"{p.Module} — {p.Title} (business wiki): {p.Labels.Replace(",", ", ")}", p.UpdatedAt ?? p.CreatedAt, p.Module)).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read the business wiki index from {Store}.", businessStore.Description);
            return [];
        }
    }

    public async Task<IReadOnlyList<MemoryItemDto>> GetManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.Distinct().Take(50).ToList();
        if (idList.Count == 0) return [];

        var results = new List<MemoryItemDto>();
        var projects = await LocalProjectsAsync(cancellationToken);
        if (shortStore.IsConnected)
        {
            foreach (var id in idList)
            {
                try
                {
                    if (await shortStore.GetAsync(id) is { } record) results.Add(ToDto(record, projects));
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not read short memory {Id}", id);
                }
            }
        }

        var remaining = idList.Except(results.Select(r => r.Id)).ToList();
        if (remaining.Count == 0) return results;

        var longs = new List<LongMemory>();
        try
        {
            await using var db = await longStore.CreateAsync(cancellationToken);
            longs = await db.LongMemories.AsNoTracking()
                .Where(m => remaining.Contains(m.Id))
                .ToListAsync(cancellationToken);

            // Usage statistics help to spot memories worth keeping.
            await db.LongMemories.Where(m => remaining.Contains(m.Id)).ExecuteUpdateAsync(s => s
                .SetProperty(m => m.HitCount, m => m.HitCount + 1)
                .SetProperty(m => m.LastAccessedAt, DateTime.UtcNow), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LongStoreFailed(ex, "Could not read long memories");
        }
        results.AddRange(longs.Select(m => ToDto(m, projects)));

        remaining = remaining.Except(longs.Select(m => m.Id)).ToList();
        if (remaining.Count > 0) results.AddRange(await BusinessPagesAsync(remaining, projects, cancellationToken));
        return results;
    }

    // Business wiki pages as the planner reads them: the label names the page, the description is the page itself
    // with images replaced by their captions.
    private async Task<IReadOnlyList<MemoryItemDto>> BusinessPagesAsync(IReadOnlyList<Guid> ids, LocalProjects projects, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await businessStore.CreateAsync(cancellationToken);
            var pages = await db.BusinessPages.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);
            return pages.Select(p =>
            {
                var content = BusinessMarkdown.ForPlanner(p.Content);
                if (content.Length > PlannerPageMaxChars) content = content[..PlannerPageMaxChars] + "\n… (page truncated)";
                var (projectId, projectName) = Owner(projects, null, p.ProjectKey, p.ProjectName);
                return new MemoryItemDto(p.Id, MemoryLevel.Business, MemoryType.Business, projectId, projectName,
                    $"{p.Module} — {p.Title}", content, null, p.CreatedAt, p.UpdatedAt, 0, p.ProjectKey, p.Module);
            }).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read business wiki pages from {Store}.", businessStore.Description);
            return [];
        }
    }

    public async Task<PagedResult<MemoryItemDto>> QueryAsync(MemoryQuery query, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = query.PageSize <= 0 ? 50 : Math.Clamp(query.PageSize, 1, 200);
        var search = query.Search?.Trim();
        var module = string.IsNullOrWhiteSpace(query.Module) ? null : query.Module.Trim();
        var items = new List<MemoryItemDto>();
        var projects = await LocalProjectsAsync(cancellationToken);

        // A project filter matches every memory of the project key, whichever developer wrote it. An unknown project
        // id still filters (to its own legacy rows) rather than showing everything.
        var scope = query.ProjectId is { } filterId ? projects.Find(filterId) ?? ProjectScope.Unknown(filterId) : null;

        if (query.Level is null or MemoryLevel.Short)
        {
            var shorts = (await SafeShortAsync()).Where(r =>
                (query.Type is null || r.Type == query.Type)
                && (scope is null || scope.Owns(r.ProjectKey, r.ProjectId))
                && (module is null || MemoryLabels.HasModule(r.Module, r.ExtraModules, module))
                && (query.Kind is null || r.Kind == query.Kind)
                && (query.Layer is null || MemoryLabels.ParseLayers(r.Layers).Contains(query.Layer.Value))
                && (query.Labeled is null || IsLabeled(r.LabelVersion, r.Module, r.Kind) == query.Labeled)
                && (query.NeedsReview is null || (r.ReviewReason is not null) == query.NeedsReview)
                && (string.IsNullOrEmpty(search)
                    || r.Label.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || r.Description.Contains(search, StringComparison.OrdinalIgnoreCase)));
            items.AddRange(shorts.Select(r => ToDto(r, projects)));
        }

        if (query.Level is null or MemoryLevel.Long)
        {
            await using var db = await longStore.CreateAsync(cancellationToken);
            var longQuery = db.LongMemories.AsNoTracking().AsQueryable();
            if (query.Type is { } type) longQuery = longQuery.Where(m => m.Type == type);
            if (scope is not null) longQuery = WhereVisible(longQuery, scope, includeKnowledge: false);
            if (module is not null)
                longQuery = longQuery.Where(m => m.Module == module || (m.ExtraModules != null && ("," + m.ExtraModules + ",").Contains("," + module + ",")));
            if (query.Kind is { } kind) longQuery = longQuery.Where(m => m.Kind == kind);
            if (query.Layer is { } layer)
            {
                var layerName = layer.ToString();
                longQuery = longQuery.Where(m => m.Layers != null && ("," + m.Layers + ",").Contains("," + layerName + ","));
            }
            if (query.Labeled == true) longQuery = longQuery.Where(m => m.LabelVersion != null && m.Module != null && m.Kind != null);
            if (query.Labeled == false) longQuery = longQuery.Where(m => m.LabelVersion == null || m.Module == null || m.Kind == null);
            if (query.NeedsReview == true) longQuery = longQuery.Where(m => m.ReviewReason != null);
            if (query.NeedsReview == false) longQuery = longQuery.Where(m => m.ReviewReason == null);
            if (!string.IsNullOrEmpty(search)) longQuery = longQuery.Where(m => m.Label.Contains(search) || m.Description.Contains(search));

            var longs = await longQuery.OrderByDescending(m => m.CreatedAt).Take(page * pageSize + 500).ToListAsync(cancellationToken);
            items.AddRange(longs.Select(m => ToDto(m, projects)));
        }

        var ordered = items.OrderByDescending(i => i.CreatedAt).ToList();
        return new PagedResult<MemoryItemDto>(ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList(), ordered.Count, page, pageSize);
    }

    public async Task<MemoryItemDto?> GetAsync(MemoryLevel level, Guid id, CancellationToken cancellationToken = default)
    {
        var projects = await LocalProjectsAsync(cancellationToken);
        if (level == MemoryLevel.Short)
        {
            var record = shortStore.IsConnected ? await shortStore.GetAsync(id) : null;
            return record is null ? null : ToDto(record, projects);
        }

        await using var db = await longStore.CreateAsync(cancellationToken);
        var entity = await db.LongMemories.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        return entity is null ? null : ToDto(entity, projects);
    }

    // Saving from the form labels an unlabeled (or to-be-reviewed) memory for good; its old label is kept as LegacyLabel.
    public async Task<MemoryItemDto?> UpdateAsync(MemoryLevel level, Guid id, SaveMemoryRequest request, CancellationToken cancellationToken = default)
    {
        var projects = await LocalProjectsAsync(cancellationToken);
        var projectId = request.Type == MemoryType.Knowledge ? null : request.ProjectId;
        var owner = projects.Find(projectId);

        if (level == MemoryLevel.Short)
        {
            EnsureShortStore();
            var record = await shortStore.GetAsync(id);
            if (record is null) return null;

            var keyOwner = owner ?? (record.ProjectKey is { } k ? new ProjectScope(Guid.Empty, k, record.ProjectName ?? string.Empty) : null);
            var label = await PrepareLabelAsync(request, request.Type == MemoryType.Knowledge ? null : keyOwner, cancellationToken);
            var updated = record with
            {
                Type = request.Type,
                ProjectId = projectId,
                ProjectKey = projectId is null ? null : owner?.Key ?? record.ProjectKey,
                ProjectName = projectId is null ? null : owner?.Name ?? record.ProjectName,
                Label = label.Composed,
                Description = request.Description.Trim(),
                Module = label.Module,
                ExtraModules = MemoryLabels.JoinCsv(label.ExtraModules),
                Layers = MemoryLabels.LayersCsv(label.Layers),
                Kind = label.Kind,
                Area = label.Area,
                Keywords = MemoryLabels.JoinCsv(label.Keywords),
                LabelVersion = MemoryLabels.CurrentVersion,
                LegacyLabel = record.LegacyLabel ?? (IsLabeled(record.LabelVersion, record.Module, record.Kind) ? null : record.Label),
                ReviewReason = null
            };
            await shortStore.SaveAsync(updated);
            Publish("updated", id);
            return ToDto(updated, projects);
        }

        await using var db = await longStore.CreateAsync(cancellationToken);
        var entity = await db.LongMemories.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (entity is null) return null;

        var longOwner = owner ?? (entity.ProjectKey is { } key ? new ProjectScope(Guid.Empty, key, entity.ProjectName ?? string.Empty) : null);
        var longLabel = await PrepareLabelAsync(request, request.Type == MemoryType.Knowledge ? null : longOwner, cancellationToken);
        if (!IsLabeled(entity.LabelVersion, entity.Module, entity.Kind)) entity.LegacyLabel ??= entity.Label;
        entity.Type = request.Type;
        // The main database enforces the project foreign key: only a project registered here can be referenced.
        entity.ProjectId = owner?.Id;
        entity.ProjectKey = projectId is null ? null : owner?.Key ?? entity.ProjectKey;
        entity.ProjectName = projectId is null ? null : owner?.Name ?? entity.ProjectName;
        entity.Description = request.Description.Trim();
        ApplyLabel(entity, longLabel);
        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        Publish("updated", id);
        return ToDto(entity, projects);
    }

    public async Task<bool> DeleteAsync(MemoryLevel level, Guid id, CancellationToken cancellationToken = default)
    {
        bool deleted;
        if (level == MemoryLevel.Short)
        {
            EnsureShortStore();
            deleted = await shortStore.DeleteAsync(id);
        }
        else
        {
            await using var db = await longStore.CreateAsync(cancellationToken);
            deleted = await db.LongMemories.Where(m => m.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;
        }

        if (deleted) Publish("deleted", id);
        return deleted;
    }

    public async Task<bool> PromoteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        EnsureShortStore();
        var record = await shortStore.GetAsync(id);
        if (record is null) return false;

        await InsertLongAsync(record, cancellationToken);
        await shortStore.DeleteAsync(id);
        Publish("promoted", id);
        activityLog.Info("memory", $"Memory '{record.Label}' dipindah ke long memory secara manual.", record.SourceThreadId, record.ProjectId);
        return true;
    }

    public async Task<int> ConsolidateAsync(CancellationToken cancellationToken = default)
    {
        if (!shortStore.IsConnected) return 0;

        var cutoff = DateTime.UtcNow.AddHours(-options.Value.ShortMemoryHours);
        var due = await shortStore.GetCreatedBeforeAsync(cutoff);
        var moved = 0;
        foreach (var record in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await InsertLongAsync(record, cancellationToken);
                await shortStore.DeleteAsync(record.Id);
                moved++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (StoreErrors.IsUnreachable(ex))
                {
                    // Every record would fail the same way; they stay in short memory until the next round.
                    LongStoreFailed(ex, $"Memory consolidation postponed ({due.Count - moved} due)");
                    break;
                }
                logger.LogWarning(ex, "Could not consolidate short memory {Id}", record.Id);
            }
        }

        if (moved > 0)
        {
            Publish("consolidated", null);
            activityLog.Info("memory", $"{moved} short memory (> {options.Value.ShortMemoryHours} jam) dipindah ke long memory.");
        }
        return moved;
    }

    public async Task<MemoryStatsDto> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var shorts = await SafeShortAsync();
        var longDescription = longStore.Description;
        var longByType = new Dictionary<MemoryType, int>();
        int longUnlabeled = 0, longReview = 0;
        try
        {
            await using var db = await longStore.CreateAsync(cancellationToken);
            longByType = await db.LongMemories.AsNoTracking()
                .GroupBy(m => m.Type)
                .Select(g => new { Type = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Type, x => x.Count, cancellationToken);
            longUnlabeled = await db.LongMemories.CountAsync(m => m.LabelVersion == null || m.Module == null || m.Kind == null, cancellationToken);
            longReview = await db.LongMemories.CountAsync(m => m.ReviewReason != null, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LongStoreFailed(ex, "Could not count long memories");
            longDescription += " — tidak terhubung";
        }

        var longCount = longByType.Values.Sum();
        var projectCount = shorts.Count(r => r.Type == MemoryType.Project) + longByType.GetValueOrDefault(MemoryType.Project);
        var knowledgeCount = shorts.Count(r => r.Type == MemoryType.Knowledge) + longByType.GetValueOrDefault(MemoryType.Knowledge);
        var unlabeled = shorts.Count(r => !IsLabeled(r.LabelVersion, r.Module, r.Kind)) + longUnlabeled;
        var review = shorts.Count(r => r.ReviewReason is not null) + longReview;
        return new MemoryStatsDto(shorts.Count, longCount, projectCount, knowledgeCount, shortStore.IsConnected,
            shortStore.Description, longDescription, unlabeled, review);
    }

    // Idempotent: the long row keeps the short memory id, so a retried consolidation never duplicates.
    private async Task<LongMemory> InsertLongAsync(ShortMemoryRecord record, CancellationToken cancellationToken)
    {
        await using var db = await longStore.CreateAsync(cancellationToken);
        var existing = await db.LongMemories.FirstOrDefaultAsync(m => m.Id == record.Id, cancellationToken);
        if (existing is not null) return existing;

        // The project may have been deleted while the memory waited in short memory. Its key keeps the memory with
        // the project's other clones; without a key (older records) it turns into knowledge as before.
        var projects = await LocalProjectsAsync(cancellationToken);
        var owner = projects.Find(record.ProjectId);
        var projectKey = record.Type == MemoryType.Knowledge ? null : record.ProjectKey ?? owner?.Key;
        var projectId = owner?.Id ?? (projectKey is null ? null : projects.FindByKey(projectKey)?.Id);

        var entity = new LongMemory
        {
            Id = record.Id,
            ProjectId = projectId,
            ProjectKey = projectKey,
            ProjectName = projectKey is null ? null : record.ProjectName ?? owner?.Name,
            Type = projectId is null && projectKey is null ? MemoryType.Knowledge : record.Type,
            Label = record.Label,
            Description = record.Description,
            Module = record.Module,
            ExtraModules = record.ExtraModules,
            Layers = record.Layers,
            Kind = record.Kind,
            Area = record.Area,
            Keywords = record.Keywords,
            LabelVersion = record.LabelVersion,
            LegacyLabel = record.LegacyLabel,
            ReviewReason = record.ReviewReason,
            RelabelAttempts = record.RelabelAttempts,
            SourceThreadId = record.SourceThreadId,
            CreatedAt = record.CreatedAt,
            PromotedAt = DateTime.UtcNow
        };
        db.LongMemories.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    // Validates and normalizes a label: the module resolved against (or added to) the registry of the memory's key.
    private async Task<PreparedLabel> PrepareLabelAsync(SaveMemoryRequest request, ProjectScope? owner, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Type) || request.Type == MemoryType.Business)
            throw new AppValidationException("Tipe memory tidak valid (business memory dikelola di tab Bisnis).");
        if (request.Type == MemoryType.Project && request.ProjectId is null)
            throw new AppValidationException("Project memory harus terkait dengan sebuah project.");
        if (string.IsNullOrWhiteSpace(request.Description)) throw new AppValidationException("Deskripsi wajib diisi.");
        if (MemoryLabels.Module(request.Module) is not { } module) throw new AppValidationException("Modul wajib diisi.");
        if (!Enum.IsDefined(request.Kind)) throw new AppValidationException("Jenis memory wajib dipilih.");
        if (!MemoryLabels.KindAllowedFor(request.Type, request.Kind))
            throw new AppValidationException("Knowledge memory hanya boleh berjenis Pitfall, Pattern, atau Architecture.");
        var layers = (request.Layers ?? []).Where(Enum.IsDefined).Distinct().Order().ToList();

        var extra = MemoryLabels.ExtraModules(request.ExtraModules, module);

        var key = request.Type == MemoryType.Knowledge ? MemoryLabels.KnowledgeKey : owner?.Key;
        if (key is not null)
        {
            try
            {
                (module, _) = await registry.EnsureAsync(key, module, cancellationToken: cancellationToken);
                var resolved = new List<string>(extra.Count);
                foreach (var name in extra) resolved.Add((await registry.EnsureAsync(key, name, cancellationToken: cancellationToken)).Name);
                // Two names may resolve to the same module (an alias of the main one, say).
                extra = MemoryLabels.ExtraModules(resolved, module);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not AppValidationException)
            {
                // The registry lives with long memory; while that store is down the names are kept as written.
                LongStoreFailed(ex, "Could not resolve the module of a memory");
            }
        }

        var keywords = MemoryLabels.Keywords(request.Keywords, [module, .. extra]);
        if (keywords.Count == 0) throw new AppValidationException("Isi minimal satu keyword.");
        var area = MemoryLabels.Area(request.Area);
        return new PreparedLabel(module, extra, layers, request.Kind, area, keywords,
            MemoryLabels.Compose(MemoryLabels.Display(area, module, layers, request.Kind, extra), keywords));
    }

    private static void ApplyLabel(LongMemory entity, PreparedLabel label)
    {
        entity.Label = label.Composed;
        entity.Module = label.Module;
        entity.ExtraModules = MemoryLabels.JoinCsv(label.ExtraModules);
        entity.Layers = MemoryLabels.LayersCsv(label.Layers);
        entity.Kind = label.Kind;
        entity.Area = label.Area;
        entity.Keywords = MemoryLabels.JoinCsv(label.Keywords);
        entity.LabelVersion = MemoryLabels.CurrentVersion;
        entity.ReviewReason = null;
    }

    private static bool IsLabeled(int? version, string? module, MemoryKind? kind) => version is not null && module is not null && kind is not null;

    private async Task<IReadOnlyList<ShortMemoryRecord>> SafeShortAsync()
    {
        if (!shortStore.IsConnected) return [];
        try
        {
            return await shortStore.GetAllAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read short memories from {Store}.", shortStore.Description);
            return [];
        }
    }

    private void EnsureShortStore()
    {
        if (!shortStore.IsConnected)
            throw new AppValidationException($"Penyimpanan short memory ({shortStore.Description}) tidak terhubung — short memory tidak dapat diakses.");
    }

    private void Publish(string action, Guid? id) =>
        eventBus.Publish(AppEvent.Create(AppEventTypes.MemoryChanged, new { id, action }));

    private async Task<LocalProjects> LocalProjectsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Projects.AsNoTracking()
            .Select(p => new { p.Id, p.ProjectKey, p.Name })
            .ToListAsync(cancellationToken);
        return new LocalProjects(rows.Select(p => new ProjectScope(p.Id, string.IsNullOrEmpty(p.ProjectKey) ? null : p.ProjectKey, p.Name)).ToList());
    }

    // Memory of the scope's project key (written by any developer) plus, for rows written before keys existed, the
    // memory of the project id itself.
    private static IQueryable<LongMemory> WhereVisible(IQueryable<LongMemory> query, ProjectScope? scope, bool includeKnowledge)
    {
        if (scope is null) return includeKnowledge ? query.Where(m => m.Type == MemoryType.Knowledge) : query.Where(_ => false);

        var key = scope.Key;
        var id = scope.Id;
        return includeKnowledge
            ? query.Where(m => m.Type == MemoryType.Knowledge || (key != null && m.ProjectKey == key) || (m.ProjectKey == null && m.ProjectId == id))
            : query.Where(m => (key != null && m.ProjectKey == key) || (m.ProjectKey == null && m.ProjectId == id));
    }

    // The memory's project as known on this machine: its own project when registered here, else (memory written by
    // another developer) this machine's clone of the same project, so the Memory menu can filter and edit it.
    private static (Guid? Id, string? Name) Owner(LocalProjects projects, Guid? projectId, string? projectKey, string? projectName)
    {
        var local = projects.Find(projectId) ?? (projectKey is null ? null : projects.FindByKey(projectKey));
        return local is null ? (projectId, projectName) : (local.Id, local.Name);
    }

    private static MemoryIndexEntry ToIndexEntry(ShortMemoryRecord r) => IsLabeled(r.LabelVersion, r.Module, r.Kind)
        ? new MemoryIndexEntry(r.Id, MemoryLevel.Short, r.Type, r.Label, r.CreatedAt, r.Module, MemoryLabels.ParseLayers(r.Layers), r.Kind, r.Area,
            MemoryLabels.SplitCsv(r.Keywords), ExtraModules: MemoryLabels.SplitCsv(r.ExtraModules))
        : new MemoryIndexEntry(r.Id, MemoryLevel.Short, r.Type, r.Label, r.CreatedAt);

    private static MemoryItemDto ToDto(ShortMemoryRecord r, LocalProjects projects)
    {
        var (projectId, projectName) = Owner(projects, r.ProjectId, r.ProjectKey, r.ProjectName);
        var labeled = IsLabeled(r.LabelVersion, r.Module, r.Kind);
        var extra = MemoryLabels.SplitCsv(r.ExtraModules);
        return new MemoryItemDto(r.Id, MemoryLevel.Short, r.Type, projectId, projectName,
            labeled ? MemoryLabels.Display(r.Area, r.Module!, MemoryLabels.ParseLayers(r.Layers), r.Kind!.Value, extra) : r.Label,
            r.Description, r.SourceThreadId, r.CreatedAt, null, 0, r.ProjectKey ?? projects.Find(r.ProjectId)?.Key,
            r.Module, MemoryLabels.ParseLayers(r.Layers), r.Kind, r.Area, MemoryLabels.SplitCsv(r.Keywords), labeled, r.LegacyLabel, r.ReviewReason, extra);
    }

    private static MemoryItemDto ToDto(LongMemory m, LocalProjects projects)
    {
        var (projectId, projectName) = Owner(projects, m.ProjectId, m.ProjectKey, m.ProjectName);
        var labeled = IsLabeled(m.LabelVersion, m.Module, m.Kind);
        var extra = MemoryLabels.SplitCsv(m.ExtraModules);
        return new MemoryItemDto(m.Id, MemoryLevel.Long, m.Type, projectId, projectName,
            labeled ? MemoryLabels.Display(m.Area, m.Module!, MemoryLabels.ParseLayers(m.Layers), m.Kind!.Value, extra) : m.Label,
            m.Description, m.SourceThreadId, m.CreatedAt, m.PromotedAt, m.HitCount, m.ProjectKey ?? projects.Find(m.ProjectId)?.Key,
            m.Module, MemoryLabels.ParseLayers(m.Layers), m.Kind, m.Area, MemoryLabels.SplitCsv(m.Keywords), labeled, m.LegacyLabel, m.ReviewReason, extra);
    }

    private sealed record PreparedLabel(string Module, IReadOnlyList<string> ExtraModules, IReadOnlyList<MemoryLayer> Layers, MemoryKind Kind, string? Area,
        IReadOnlyList<string> Keywords, string Composed);

    // Key is null for a project registered before keys existed and not seen since (the startup backfill fills it).
    private sealed record ProjectScope(Guid Id, string? Key, string Name)
    {
        public static ProjectScope Unknown(Guid id) => new(id, null, string.Empty);

        public bool Owns(string? projectKey, Guid? projectId) =>
            (Key is not null && projectKey == Key) || (projectKey is null && projectId == Id);
    }

    private sealed class LocalProjects(IReadOnlyList<ProjectScope> projects)
    {
        private readonly Dictionary<Guid, ProjectScope> _byId = projects.ToDictionary(p => p.Id);

        // Several clones of one repository may be registered here; the first one stands for the key.
        private readonly Dictionary<string, ProjectScope> _byKey = projects
            .Where(p => p.Key is not null)
            .GroupBy(p => p.Key!)
            .ToDictionary(g => g.Key, g => g.First());

        public ProjectScope? Find(Guid? id) => id is { } value ? _byId.GetValueOrDefault(value) : null;

        public ProjectScope? FindByKey(string key) => _byKey.GetValueOrDefault(key);
    }
}
