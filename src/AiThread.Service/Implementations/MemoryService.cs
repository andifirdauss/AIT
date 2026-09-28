using System.Text.RegularExpressions;
using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Common.Options;
using AiThread.Data.Cache;
using AiThread.Data.Context;
using AiThread.Data.Messaging;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryService(
    IDbContextFactory<AiThreadDbContext> dbFactory,
    IShortMemoryStore shortStore,
    IEventBus eventBus,
    IActivityLogService activityLog,
    IOptions<AiThreadOptions> options,
    ILogger<MemoryService> logger) : IMemoryService
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "that", "this", "from", "into", "when", "then", "than", "are", "was", "will",
        "yang", "dan", "untuk", "dengan", "ini", "itu", "dari", "pada", "akan", "juga", "tidak", "ada", "bisa",
        "tolong", "buat", "buatkan", "tambah", "tambahkan", "ubah", "please", "make", "add", "update", "change"
    };

    public async Task<MemoryItemDto> AddShortAsync(SaveMemoryRequest request, Guid? sourceThreadId = null, CancellationToken cancellationToken = default)
    {
        Validate(request);
        var record = new ShortMemoryRecord(
            Guid.NewGuid(),
            request.Type == MemoryType.Knowledge ? null : request.ProjectId,
            request.Type,
            request.Label.Trim(),
            request.Description.Trim(),
            sourceThreadId,
            DateTime.UtcNow);

        if (shortStore.IsConnected)
        {
            try
            {
                await shortStore.SaveAsync(record);
                Publish("created", record.Id);
                return await ToDtoAsync(record, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Redis write failed; storing memory directly as long memory.");
            }
        }

        // Redis unavailable: keep the memory rather than losing it.
        var entity = await InsertLongAsync(record, cancellationToken);
        activityLog.Warn("memory", $"Redis tidak tersedia — memory '{record.Label}' langsung disimpan sebagai long memory.", sourceThreadId, record.ProjectId);
        Publish("created", entity.Id);
        return ToDto(entity, await ProjectNameAsync(entity.ProjectId, cancellationToken));
    }

    public async Task<IReadOnlyList<MemoryIndexEntry>> GetIndexAsync(Guid? projectId, string? query, int take, CancellationToken cancellationToken = default)
    {
        bool InScope(MemoryType type, Guid? owner) => type == MemoryType.Knowledge || (projectId is not null && owner == projectId);

        var entries = (await SafeShortAsync())
            .Where(r => InScope(r.Type, r.ProjectId))
            .Select(r => new MemoryIndexEntry(r.Id, MemoryLevel.Short, r.Type, r.Label, r.CreatedAt))
            .ToList();

        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            var longEntries = await db.LongMemories.AsNoTracking()
                .Where(m => m.Type == MemoryType.Knowledge || (projectId != null && m.ProjectId == projectId))
                .Select(m => new MemoryIndexEntry(m.Id, MemoryLevel.Long, m.Type, m.Label, m.CreatedAt))
                .ToListAsync(cancellationToken);
            entries.AddRange(longEntries);
        }

        var keywords = Keywords(query);
        if (keywords.Count == 0)
        {
            return entries
                .OrderBy(e => e.Level)
                .ThenByDescending(e => e.CreatedAt)
                .Take(take)
                .ToList();
        }

        return entries
            .Select(e => (Entry: e, Score: keywords.Count(k => e.Label.Contains(k, StringComparison.OrdinalIgnoreCase))))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Entry.Level)
            .ThenByDescending(x => x.Entry.CreatedAt)
            .Take(take)
            .Select(x => x.Entry)
            .ToList();
    }

    public async Task<IReadOnlyList<MemoryItemDto>> GetManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.Distinct().Take(50).ToList();
        if (idList.Count == 0) return [];

        var results = new List<MemoryItemDto>();
        if (shortStore.IsConnected)
        {
            foreach (var id in idList)
            {
                try
                {
                    if (await shortStore.GetAsync(id) is { } record) results.Add(await ToDtoAsync(record, cancellationToken));
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not read short memory {Id}", id);
                }
            }
        }

        var remaining = idList.Except(results.Select(r => r.Id)).ToList();
        if (remaining.Count == 0) return results;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var longs = await db.LongMemories.AsNoTracking().Include(m => m.Project)
            .Where(m => remaining.Contains(m.Id))
            .ToListAsync(cancellationToken);
        results.AddRange(longs.Select(m => ToDto(m, m.Project?.Name)));

        // Usage statistics help to spot memories worth keeping.
        await db.LongMemories.Where(m => remaining.Contains(m.Id)).ExecuteUpdateAsync(s => s
            .SetProperty(m => m.HitCount, m => m.HitCount + 1)
            .SetProperty(m => m.LastAccessedAt, DateTime.UtcNow), cancellationToken);

        return results;
    }

    public async Task<PagedResult<MemoryItemDto>> QueryAsync(MemoryQuery query, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = query.PageSize <= 0 ? 50 : Math.Clamp(query.PageSize, 1, 200);
        var search = query.Search?.Trim();
        var items = new List<MemoryItemDto>();

        if (query.Level is null or MemoryLevel.Short)
        {
            var shorts = (await SafeShortAsync()).Where(r =>
                (query.Type is null || r.Type == query.Type)
                && (query.ProjectId is null || r.ProjectId == query.ProjectId)
                && (string.IsNullOrEmpty(search)
                    || r.Label.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || r.Description.Contains(search, StringComparison.OrdinalIgnoreCase)));
            var names = await ProjectNamesAsync(cancellationToken);
            items.AddRange(shorts.Select(r => ToDto(r, r.ProjectId is { } pid ? names.GetValueOrDefault(pid) : null)));
        }

        if (query.Level is null or MemoryLevel.Long)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var longQuery = db.LongMemories.AsNoTracking().Include(m => m.Project).AsQueryable();
            if (query.Type is { } type) longQuery = longQuery.Where(m => m.Type == type);
            if (query.ProjectId is { } projectId) longQuery = longQuery.Where(m => m.ProjectId == projectId);
            if (!string.IsNullOrEmpty(search)) longQuery = longQuery.Where(m => m.Label.Contains(search) || m.Description.Contains(search));

            var longs = await longQuery.OrderByDescending(m => m.CreatedAt).Take(page * pageSize + 500).ToListAsync(cancellationToken);
            items.AddRange(longs.Select(m => ToDto(m, m.Project?.Name)));
        }

        var ordered = items.OrderByDescending(i => i.CreatedAt).ToList();
        return new PagedResult<MemoryItemDto>(ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList(), ordered.Count, page, pageSize);
    }

    public async Task<MemoryItemDto?> GetAsync(MemoryLevel level, Guid id, CancellationToken cancellationToken = default)
    {
        if (level == MemoryLevel.Short)
        {
            var record = shortStore.IsConnected ? await shortStore.GetAsync(id) : null;
            return record is null ? null : await ToDtoAsync(record, cancellationToken);
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.LongMemories.AsNoTracking().Include(m => m.Project).FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        return entity is null ? null : ToDto(entity, entity.Project?.Name);
    }

    public async Task<MemoryItemDto?> UpdateAsync(MemoryLevel level, Guid id, SaveMemoryRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        var projectId = request.Type == MemoryType.Knowledge ? null : request.ProjectId;

        if (level == MemoryLevel.Short)
        {
            EnsureRedis();
            var record = await shortStore.GetAsync(id);
            if (record is null) return null;

            var updated = record with { Type = request.Type, ProjectId = projectId, Label = request.Label.Trim(), Description = request.Description.Trim() };
            await shortStore.SaveAsync(updated);
            Publish("updated", id);
            return await ToDtoAsync(updated, cancellationToken);
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.LongMemories.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (entity is null) return null;

        entity.Type = request.Type;
        entity.ProjectId = projectId;
        entity.Label = request.Label.Trim();
        entity.Description = request.Description.Trim();
        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        Publish("updated", id);
        return ToDto(entity, await ProjectNameAsync(entity.ProjectId, cancellationToken));
    }

    public async Task<bool> DeleteAsync(MemoryLevel level, Guid id, CancellationToken cancellationToken = default)
    {
        bool deleted;
        if (level == MemoryLevel.Short)
        {
            EnsureRedis();
            deleted = await shortStore.DeleteAsync(id);
        }
        else
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            deleted = await db.LongMemories.Where(m => m.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;
        }

        if (deleted) Publish("deleted", id);
        return deleted;
    }

    public async Task<bool> PromoteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        EnsureRedis();
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
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var longByType = await db.LongMemories.AsNoTracking()
            .GroupBy(m => m.Type)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var longCount = longByType.Sum(x => x.Count);
        var projectCount = shorts.Count(r => r.Type == MemoryType.Project) + longByType.Where(x => x.Type == MemoryType.Project).Sum(x => x.Count);
        var knowledgeCount = shorts.Count(r => r.Type == MemoryType.Knowledge) + longByType.Where(x => x.Type == MemoryType.Knowledge).Sum(x => x.Count);
        return new MemoryStatsDto(shorts.Count, longCount, projectCount, knowledgeCount, shortStore.IsConnected);
    }

    // Idempotent: the long row keeps the short memory id, so a retried consolidation never duplicates.
    private async Task<LongMemory> InsertLongAsync(ShortMemoryRecord record, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.LongMemories.FirstOrDefaultAsync(m => m.Id == record.Id, cancellationToken);
        if (existing is not null) return existing;

        // The project may have been deleted while the memory waited in Redis.
        var projectId = record.ProjectId;
        if (projectId is { } pid && !await db.Projects.AnyAsync(p => p.Id == pid, cancellationToken)) projectId = null;

        var entity = new LongMemory
        {
            Id = record.Id,
            ProjectId = projectId,
            Type = projectId is null ? MemoryType.Knowledge : record.Type,
            Label = record.Label,
            Description = record.Description,
            SourceThreadId = record.SourceThreadId,
            CreatedAt = record.CreatedAt,
            PromotedAt = DateTime.UtcNow
        };
        db.LongMemories.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    private async Task<IReadOnlyList<ShortMemoryRecord>> SafeShortAsync()
    {
        if (!shortStore.IsConnected) return [];
        try
        {
            return await shortStore.GetAllAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read short memories from Redis.");
            return [];
        }
    }

    private void EnsureRedis()
    {
        if (!shortStore.IsConnected) throw new AppValidationException("Redis tidak terhubung — short memory tidak dapat diakses.");
    }

    private void Publish(string action, Guid? id) =>
        eventBus.Publish(AppEvent.Create(AppEventTypes.MemoryChanged, new { id, action }));

    private async Task<Dictionary<Guid, string>> ProjectNamesAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Projects.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
    }

    private async Task<string?> ProjectNameAsync(Guid? projectId, CancellationToken cancellationToken)
    {
        if (projectId is null) return null;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Projects.AsNoTracking().Where(p => p.Id == projectId).Select(p => p.Name).FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<MemoryItemDto> ToDtoAsync(ShortMemoryRecord record, CancellationToken cancellationToken) =>
        ToDto(record, await ProjectNameAsync(record.ProjectId, cancellationToken));

    private static MemoryItemDto ToDto(ShortMemoryRecord r, string? projectName) => new(
        r.Id, MemoryLevel.Short, r.Type, r.ProjectId, projectName, r.Label, r.Description, r.SourceThreadId, r.CreatedAt, null, 0);

    private static MemoryItemDto ToDto(LongMemory m, string? projectName) => new(
        m.Id, MemoryLevel.Long, m.Type, m.ProjectId, projectName, m.Label, m.Description, m.SourceThreadId, m.CreatedAt, m.PromotedAt, m.HitCount);

    private static void Validate(SaveMemoryRequest request)
    {
        if (!Enum.IsDefined(request.Type)) throw new AppValidationException("Tipe memory tidak valid.");
        if (string.IsNullOrWhiteSpace(request.Label)) throw new AppValidationException("Label wajib diisi.");
        if (request.Label.Length > 300) throw new AppValidationException("Label maksimal 300 karakter.");
        if (string.IsNullOrWhiteSpace(request.Description)) throw new AppValidationException("Deskripsi wajib diisi.");
        if (request.Type == MemoryType.Project && request.ProjectId is null)
            throw new AppValidationException("Project memory harus terkait dengan sebuah project.");
    }

    public static IReadOnlyList<string> Keywords(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        return WordPattern().Matches(text)
            .Select(m => m.Value.ToLowerInvariant())
            .Where(w => w.Length >= 3 && !StopWords.Contains(w))
            .Distinct()
            .Take(40)
            .ToList();
    }

    [GeneratedRegex(@"[\p{L}\p{N}_]+")]
    private static partial Regex WordPattern();
}
