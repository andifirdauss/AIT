using System.Reflection;
using System.Text.RegularExpressions;
using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Data.Cache;
using AiThread.Data.Context;
using AiThread.Data.Memory;
using AiThread.Data.Messaging;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

// Memory travels by ProjectKey, so a file exported on one machine lands on the same project on another, whatever its
// root folder. The rules follow the copy made when switching stores: ids are kept (re-importing never duplicates),
// a project that is not registered here is dropped from the row but its key stays, and project memory without any
// key becomes knowledge.
public sealed partial class MemoryTransferService(
    IDbContextFactory<AiThreadDbContext> dbFactory,
    IShortMemoryStore shortStore,
    LongMemoryStore longStore,
    IEventBus eventBus,
    IActivityLogService activityLog,
    ILogger<MemoryTransferService> logger) : IMemoryTransferService
{
    private const int MaxItems = 100_000;
    private const int BatchSize = 200;
    private const int MaxListedWarnings = 5;

    public async Task<MemoryExportFile> ExportAsync(MemoryQuery filter, CancellationToken cancellationToken = default)
    {
        var projects = await LocalProjectsAsync(cancellationToken);
        var scope = filter.ProjectId is { } projectId ? projects.Find(projectId) ?? new LocalProject(projectId, null, string.Empty) : null;
        var search = filter.Search?.Trim();
        var items = new List<MemoryExportItem>();

        if (filter.Level is null or MemoryLevel.Short)
        {
            if (!shortStore.IsConnected)
                throw new AppValidationException("Penyimpanan short memory tidak terhubung — pilih level Long untuk mengekspor long memory saja.");

            items.AddRange((await shortStore.GetAllAsync())
                .Where(r => (filter.Type is null || r.Type == filter.Type)
                    && (scope is null || scope.Owns(r.ProjectKey, r.ProjectId))
                    && Matches(search, r.Label, r.Description))
                .Select(r => ToItem(projects, r.Id, MemoryLevel.Short, r.Type, r.ProjectId, r.ProjectKey, r.ProjectName,
                    r.Label, r.Description, r.SourceThreadId, r.CreatedAt, null, 0, null)));
        }

        if (filter.Level is null or MemoryLevel.Long)
        {
            await using var db = await longStore.CreateAsync(cancellationToken);
            var query = db.LongMemories.AsNoTracking();
            if (filter.Type is { } type) query = query.Where(m => m.Type == type);
            if (scope is not null)
            {
                var key = scope.Key;
                var id = scope.Id;
                query = query.Where(m => (key != null && m.ProjectKey == key) || (m.ProjectKey == null && m.ProjectId == id));
            }
            if (!string.IsNullOrEmpty(search)) query = query.Where(m => m.Label.Contains(search) || m.Description.Contains(search));

            items.AddRange((await query.ToListAsync(cancellationToken)).Select(m => ToItem(projects, m.Id, MemoryLevel.Long, m.Type,
                m.ProjectId, m.ProjectKey, m.ProjectName, m.Label, m.Description, m.SourceThreadId, m.CreatedAt, m.PromotedAt,
                m.HitCount, m.LastAccessedAt)));
        }

        return new MemoryExportFile(MemoryExportFile.FormatName, MemoryExportFile.CurrentVersion, DateTime.UtcNow, AppVersion(),
            items.OrderBy(i => i.CreatedAt).ToList());
    }

    public async Task<MemoryImportResultDto> ImportAsync(MemoryExportFile file, MemoryImportMode mode, bool dryRun, CancellationToken cancellationToken = default)
    {
        if (file is null || file.Format != MemoryExportFile.FormatName)
            throw new AppValidationException($"Bukan file export memory AIT (format harus \"{MemoryExportFile.FormatName}\").");
        if (file.Version > MemoryExportFile.CurrentVersion)
            throw new AppValidationException($"File dibuat oleh AIT yang lebih baru (format v{file.Version}) — perbarui AIT dulu.");
        if (!Enum.IsDefined(mode)) throw new AppValidationException("Mode import tidak valid.");

        var items = file.Items ?? [];
        if (items.Count > MaxItems) throw new AppValidationException($"File berisi {items.Count} memory; maksimal {MaxItems} per import.");

        var projects = await LocalProjectsAsync(cancellationToken);
        var shortConnected = shortStore.IsConnected;
        var existingShort = shortConnected ? (await shortStore.GetAllAsync()).ToDictionary(r => r.Id) : new Dictionary<Guid, ShortMemoryRecord>();

        List<ExistingLong> existingLong;
        await using (var db = await longStore.CreateAsync(cancellationToken))
        {
            existingLong = await db.LongMemories.AsNoTracking()
                .Select(m => new ExistingLong(m.Id, m.ProjectKey, m.ProjectId, m.Type, m.Label, m.Description))
                .ToListAsync(cancellationToken);
        }
        var existingLongIds = existingLong.Select(m => m.Id).ToHashSet();

        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in existingShort.Values) fingerprints.Add(Fingerprint(ScopeOf(projects, r.Type, r.ProjectKey, r.ProjectId), r.Type, r.Label, r.Description));
        foreach (var m in existingLong) fingerprints.Add(Fingerprint(ScopeOf(projects, m.Type, m.ProjectKey, m.ProjectId), m.Type, m.Label, m.Description));

        var seenIds = new HashSet<Guid>();
        var adds = new List<Prepared>();
        var updates = new List<Prepared>();
        var invalid = new List<string>();
        int skipped = 0, duplicates = 0, orphaned = 0, foreignLocal = 0;
        var keys = new Dictionary<string, (string? Name, int Count, bool Registered)>(StringComparer.Ordinal);

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (Invalid(item) is { } reason)
            {
                invalid.Add($"Memory #{i + 1}: {reason}");
                continue;
            }

            var prepared = Prepare(item, projects);
            if (prepared.Orphaned) orphaned++;
            if (prepared.Key is { } key)
            {
                var registered = projects.FindByKey(key) is not null;
                if (!registered && ProjectKeyResolver.IsLocal(key)) foreignLocal++;
                var entry = keys.GetValueOrDefault(key, (Name: prepared.Name, Count: 0, Registered: registered));
                keys[key] = (entry.Name ?? prepared.Name, entry.Count + 1, registered);
            }

            if (!seenIds.Add(item.Id))
            {
                duplicates++;
                continue;
            }

            var fingerprint = Fingerprint(prepared.Key ?? prepared.ProjectId?.ToString(), prepared.Type, prepared.Label, prepared.Description);
            if (existingShort.ContainsKey(item.Id) || existingLongIds.Contains(item.Id))
            {
                if (mode == MemoryImportMode.Overwrite) updates.Add(prepared);
                else skipped++;
            }
            else if (!fingerprints.Add(fingerprint))
            {
                // Same memory under another id, e.g. from a file exported on another machine.
                duplicates++;
            }
            else
            {
                adds.Add(prepared);
            }
        }

        var warnings = invalid.Take(MaxListedWarnings).ToList();
        if (invalid.Count > MaxListedWarnings) warnings.Add($"… dan {invalid.Count - MaxListedWarnings} memory tidak valid lainnya.");
        if (orphaned > 0) warnings.Add($"{orphaned} project memory tanpa project key dan tanpa project yang terdaftar di sini dijadikan knowledge memory.");
        if (foreignLocal > 0) warnings.Add($"{foreignLocal} memory memakai key lokal (local:…) mesin lain, jadi tidak cocok dengan project mana pun di sini. Beri project key bersama di mesin asal (remote git, key manual, atau .aithread/project.json), lalu ekspor ulang.");
        var shortToLong = shortConnected ? 0 : adds.Count(a => a.Level == MemoryLevel.Short);
        if (shortToLong > 0) warnings.Add($"Penyimpanan short memory tidak terhubung — {shortToLong} short memory disimpan langsung sebagai long memory.");

        var projectSummary = keys
            .Select(k => new MemoryImportProjectDto(k.Key, k.Value.Name, k.Value.Count, k.Value.Registered))
            .OrderByDescending(p => p.Count)
            .ToList();
        var result = new MemoryImportResultDto(dryRun, items.Count, adds.Count, updates.Count, skipped, duplicates, invalid.Count, projectSummary, warnings);
        if (dryRun || (adds.Count == 0 && updates.Count == 0)) return result;

        try
        {
            await WriteAsync(adds, updates, shortConnected, existingShort, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Memory import stopped.");
            throw new AppValidationException($"Import berhenti: {MemoryStoreTester.ErrorMessage(ex)}. Memory yang sudah masuk tidak akan digandakan bila import diulang.");
        }

        eventBus.Publish(AppEvent.Create(AppEventTypes.MemoryChanged, new { id = (Guid?)null, action = "imported" }));
        activityLog.Info("memory", $"Import memory: {adds.Count} ditambahkan, {updates.Count} ditimpa, {skipped + duplicates} dilewati.");
        return result;
    }

    private async Task WriteAsync(List<Prepared> adds, List<Prepared> updates, bool shortConnected, Dictionary<Guid, ShortMemoryRecord> existingShort,
        CancellationToken cancellationToken)
    {
        var toLong = new List<Prepared>();
        foreach (var item in adds)
        {
            if (item.Level == MemoryLevel.Short && shortConnected) await shortStore.SaveAsync(ToRecord(item));
            else toLong.Add(item);
        }

        foreach (var item in updates.Where(u => existingShort.ContainsKey(u.Id)))
        {
            await shortStore.SaveAsync(existingShort[item.Id] with
            {
                Type = item.Type,
                ProjectId = item.ProjectId,
                ProjectKey = item.Key,
                ProjectName = item.Name,
                Label = item.Label,
                Description = item.Description
            });
        }

        var longUpdates = updates.Where(u => !existingShort.ContainsKey(u.Id)).ToDictionary(u => u.Id);
        if (toLong.Count == 0 && longUpdates.Count == 0) return;

        await using var db = await longStore.CreateAsync(cancellationToken);
        foreach (var batch in toLong.Chunk(BatchSize))
        {
            db.LongMemories.AddRange(batch.Select(item => new LongMemory
            {
                Id = item.Id,
                ProjectId = item.ProjectId,
                ProjectKey = item.Key,
                ProjectName = item.Name,
                Type = item.Type,
                Label = item.Label,
                Description = item.Description,
                SourceThreadId = item.SourceThreadId,
                CreatedAt = item.CreatedAt,
                PromotedAt = item.PromotedAt ?? DateTime.UtcNow,
                HitCount = item.HitCount,
                LastAccessedAt = item.LastAccessedAt
            }));
            await db.SaveChangesAsync(cancellationToken);
        }

        foreach (var batch in longUpdates.Keys.Chunk(BatchSize))
        {
            var rows = await db.LongMemories.Where(m => batch.Contains(m.Id)).ToListAsync(cancellationToken);
            foreach (var row in rows)
            {
                var item = longUpdates[row.Id];
                row.Type = item.Type;
                row.ProjectId = item.ProjectId;
                row.ProjectKey = item.Key;
                row.ProjectName = item.Name;
                row.Label = item.Label;
                row.Description = item.Description;
                row.UpdatedAt = DateTime.UtcNow;
            }
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static string? Invalid(MemoryExportItem? item)
    {
        if (item is null) return "kosong.";
        if (item.Id == Guid.Empty) return "id kosong.";
        if (item.Level is not (MemoryLevel.Short or MemoryLevel.Long)) return "level harus Short atau Long (business memory tidak termasuk).";
        if (item.Type is not (MemoryType.Project or MemoryType.Knowledge)) return "tipe harus Project atau Knowledge.";
        if (string.IsNullOrWhiteSpace(item.Label)) return "label kosong.";
        if (item.Label.Trim().Length > 300) return "label lebih dari 300 karakter.";
        if (string.IsNullOrWhiteSpace(item.Description)) return "deskripsi kosong.";
        if (item.ProjectKey?.Length > 300) return "project key lebih dari 300 karakter.";
        return null;
    }

    // Resolves the memory's project on this machine: the same project id (a restore on the machine that exported it)
    // when its key agrees, else a clone registered under the same key.
    private static Prepared Prepare(MemoryExportItem item, LocalProjects projects)
    {
        var knowledge = item.Type == MemoryType.Knowledge;
        var projectKey = string.IsNullOrWhiteSpace(item.ProjectKey) ? null : item.ProjectKey.Trim();
        var local = knowledge ? null
            : projects.Find(item.ProjectId) is { } byId && (projectKey is null || byId.Key is null || byId.Key == projectKey) ? byId
            : projectKey is null ? null : projects.FindByKey(projectKey);
        var key = knowledge ? null : projectKey ?? local?.Key;
        var orphaned = !knowledge && key is null && local is null;
        var type = orphaned ? MemoryType.Knowledge : item.Type;
        var name = type == MemoryType.Knowledge ? null : Trim(item.ProjectName, 200) ?? local?.Name;

        return new Prepared(item.Id, item.Level, type, local?.Id, key, name, item.Label.Trim(), item.Description.Trim(),
            item.SourceThreadId, Utc(item.CreatedAt), item.PromotedAt is { } promoted ? Utc(promoted) : null, Math.Max(0, item.HitCount),
            item.LastAccessedAt is { } accessed ? Utc(accessed) : null, orphaned);
    }

    private static ShortMemoryRecord ToRecord(Prepared item) =>
        new(item.Id, item.ProjectId, item.Type, item.Label, item.Description, item.SourceThreadId, item.CreatedAt, item.Key, item.Name);

    // Rows written before keys existed carry only the project id; the export names their project by the key it has
    // on this machine.
    private static MemoryExportItem ToItem(LocalProjects projects, Guid id, MemoryLevel level, MemoryType type, Guid? projectId,
        string? projectKey, string? projectName, string label, string description, Guid? sourceThreadId, DateTime createdAt,
        DateTime? promotedAt, int hitCount, DateTime? lastAccessedAt)
    {
        var local = type == MemoryType.Knowledge ? null : projects.Find(projectId);
        return new MemoryExportItem(id, level, type, type == MemoryType.Knowledge ? null : projectId,
            projectKey ?? local?.Key, projectName ?? local?.Name, label, description, sourceThreadId,
            createdAt, promotedAt, hitCount, lastAccessedAt);
    }

    private static string? ScopeOf(LocalProjects projects, MemoryType type, string? projectKey, Guid? projectId) =>
        type == MemoryType.Knowledge ? null : projectKey ?? projects.Find(projectId)?.Key ?? projectId?.ToString();

    // Two memories are the same when they say the same thing about the same project, ignoring case and spacing.
    private static string Fingerprint(string? scope, MemoryType type, string label, string description) =>
        $"{scope}\n{type}\n{Normalize(label)}\n{Normalize(description)}";

    private static string Normalize(string text) => Whitespace().Replace(text.Trim(), " ").ToLowerInvariant();

    private static bool Matches(string? search, string label, string description) =>
        string.IsNullOrEmpty(search)
        || label.Contains(search, StringComparison.OrdinalIgnoreCase)
        || description.Contains(search, StringComparison.OrdinalIgnoreCase);

    private static DateTime Utc(DateTime value) => value == default ? DateTime.UtcNow
        : value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
        : value.ToUniversalTime();

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }

    private static string? AppVersion() =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0];

    private async Task<LocalProjects> LocalProjectsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Projects.AsNoTracking().Select(p => new { p.Id, p.ProjectKey, p.Name }).ToListAsync(cancellationToken);
        return new LocalProjects(rows.Select(p => new LocalProject(p.Id, string.IsNullOrEmpty(p.ProjectKey) ? null : p.ProjectKey, p.Name)).ToList());
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private sealed record ExistingLong(Guid Id, string? ProjectKey, Guid? ProjectId, MemoryType Type, string Label, string Description);

    private sealed record Prepared(Guid Id, MemoryLevel Level, MemoryType Type, Guid? ProjectId, string? Key, string? Name,
        string Label, string Description, Guid? SourceThreadId, DateTime CreatedAt, DateTime? PromotedAt, int HitCount,
        DateTime? LastAccessedAt, bool Orphaned);

    private sealed record LocalProject(Guid Id, string? Key, string Name)
    {
        public bool Owns(string? projectKey, Guid? projectId) =>
            (Key is not null && projectKey == Key) || (projectKey is null && projectId == Id);
    }

    private sealed class LocalProjects(IReadOnlyList<LocalProject> projects)
    {
        private readonly Dictionary<Guid, LocalProject> _byId = projects.ToDictionary(p => p.Id);

        // Several clones of one repository may be registered here; the first one stands for the key.
        private readonly Dictionary<string, LocalProject> _byKey = projects
            .Where(p => p.Key is not null)
            .GroupBy(p => p.Key!)
            .ToDictionary(g => g.Key, g => g.First());

        public LocalProject? Find(Guid? id) => id is { } value ? _byId.GetValueOrDefault(value) : null;

        public LocalProject? FindByKey(string key) => _byKey.GetValueOrDefault(key);
    }
}
