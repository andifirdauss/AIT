using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Common.Memory;
using AiThread.Data.Cache;
using AiThread.Data.Context;
using AiThread.Data.Memory;
using AiThread.Service.Interfaces;
using AiThread.Service.Memory;
using AiThread.Service.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

public sealed class MemoryCatalogService(
    IDbContextFactory<AiThreadDbContext> dbFactory,
    IShortMemoryStore shortStore,
    LongMemoryStore longStore,
    ModuleRegistry registry,
    ModuleMerger merger,
    CodeMapper codeMapper,
    MemoryRelabeler relabeler,
    IMemoryService memoryService,
    IProjectService projectService,
    ISettingService settingService,
    IHostApplicationLifetime lifetime,
    ILogger<MemoryCatalogService> logger) : IMemoryCatalogService
{
    private const int ScanMaxFiles = 3_000;

    public async Task<IReadOnlyList<ProjectModuleDto>> GetModulesAsync(Guid? projectId, bool knowledge, CancellationToken cancellationToken = default)
    {
        var key = await KeyAsync(projectId, knowledge, cancellationToken);
        var modules = await registry.ListAsync(key, cancellationToken: cancellationToken);

        Dictionary<string, int> files;
        await using (var catalog = await longStore.CreateCatalogAsync(cancellationToken))
            files = await catalog.CodeMapEntries.AsNoTracking().Where(e => e.ProjectKey == key)
                .GroupBy(e => e.Module).Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var memories = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using var db = await longStore.CreateAsync(cancellationToken);
            var rows = await (knowledge ? db.LongMemories.Where(m => m.Type == MemoryType.Knowledge) : db.LongMemories.Where(m => m.ProjectKey == key))
                .Where(m => m.Module != null).GroupBy(m => m.Module!).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
            foreach (var row in rows) memories[row.Key] = row.Count;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogStoreWarning(ex, "Could not count memories per module");
        }
        foreach (var record in await ShortAsync())
        {
            if (record.Module is null) continue;
            if (knowledge ? record.Type != MemoryType.Knowledge : record.ProjectKey != key) continue;
            memories[record.Module] = memories.GetValueOrDefault(record.Module) + 1;
        }

        return modules.Select(m => new ProjectModuleDto(m.Id, m.Name, MemoryLabels.SplitCsv(m.Aliases), m.Description,
            memories.GetValueOrDefault(m.Name), files.GetValueOrDefault(m.Name))).ToList();
    }

    public async Task<ProjectModuleDto?> UpdateModuleAsync(Guid? projectId, bool knowledge, string name, SaveModuleRequest request, CancellationToken cancellationToken = default)
    {
        var key = await KeyAsync(projectId, knowledge, cancellationToken);
        var module = await registry.UpdateAsync(key, name, request.Description, request.Aliases, cancellationToken);
        if (module is null) return null;
        return (await GetModulesAsync(projectId, knowledge, cancellationToken)).FirstOrDefault(m => m.Id == module.Id);
    }

    public async Task<ModuleMergeDto> MergeAsync(MergeModulesRequest request, bool knowledge, CancellationToken cancellationToken = default)
    {
        var key = await KeyAsync(request.ProjectId, knowledge, cancellationToken);
        var log = await merger.MergeAsync(key, request.From, request.Into, "Digabung manual.", null, await DeciderAsync(cancellationToken), cancellationToken);
        return ModuleMerger.ToDto(log);
    }

    public async Task<IReadOnlyList<ModuleMergeDto>> GetMergesAsync(Guid? projectId, bool knowledge, CancellationToken cancellationToken = default) =>
        await merger.ListAsync(await KeyAsync(projectId, knowledge, cancellationToken), null, 100, cancellationToken);

    public async Task UndoMergeAsync(Guid id, CancellationToken cancellationToken = default) =>
        await merger.UndoAsync(id, await DeciderAsync(cancellationToken), cancellationToken);

    public async Task AcceptMergeAsync(Guid id, CancellationToken cancellationToken = default) =>
        await merger.AcceptAsync(id, await DeciderAsync(cancellationToken), cancellationToken);

    public async Task RejectMergeAsync(Guid id, CancellationToken cancellationToken = default) =>
        await merger.RejectAsync(id, await DeciderAsync(cancellationToken), cancellationToken);

    public async Task<int> CheckModulesAsync(Guid? projectId, bool knowledge, CancellationToken cancellationToken = default) =>
        await merger.CheckAsync(await KeyAsync(projectId, knowledge, cancellationToken), null, cancellationToken);

    public async Task<IReadOnlyList<CodeMapEntryDto>> GetCodeMapAsync(Guid projectId, string? search, string? module, CancellationToken cancellationToken = default)
    {
        var key = await KeyAsync(projectId, false, cancellationToken);
        var rows = await codeMapper.ListAsync(key, search, module, 500, cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    public async Task<CodeMapEntryDto> SetCodeMapAsync(Guid projectId, SetCodeMapRequest request, CancellationToken cancellationToken = default)
    {
        var project = await ContextAsync(projectId, cancellationToken);
        var target = request.Path;
        if (!target.Contains('/') && !target.Contains('\\') && !Path.HasExtension(target))
        {
            var found = await codeMapper.ResolveReferencesAsync(project, target, cancellationToken);
            target = found.Count switch
            {
                0 => throw new AppValidationException($"Tidak ada file bernama '{request.Path}' di project ini."),
                1 => found[0],
                _ => throw new AppValidationException($"'{request.Path}' cocok dengan beberapa file ({string.Join(", ", found.Take(5))}). Isi path lengkapnya.")
            };
        }
        if (string.IsNullOrWhiteSpace(request.Module)) throw new AppValidationException("Modul wajib diisi.");
        var hit = await codeMapper.SetManualAsync(project, target, request.Module, request.Layer, cancellationToken);
        var stored = await codeMapper.StoredAsync(project.Key, [hit.Path], cancellationToken);
        return ToDto(stored[hit.Path]);
    }

    public async Task<CodeMapScanResultDto> ScanCodeMapAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await ContextAsync(projectId, cancellationToken);
        var (files, mapped) = await codeMapper.ScanAsync(project, ScanMaxFiles, cancellationToken);
        var modules = (await registry.ListAsync(project.Key, cancellationToken: cancellationToken)).Count;
        return new CodeMapScanResultDto(files, mapped, modules);
    }

    public async Task<MemoryReviewDto> GetReviewAsync(Guid? projectId, CancellationToken cancellationToken = default)
    {
        var memories = (await memoryService.QueryAsync(new MemoryQuery(null, null, projectId, null, 1, 200, NeedsReview: true), cancellationToken)).Items;

        var merges = new List<ModuleMergeDto>();
        if (projectId is not null) merges.AddRange(await merger.ListAsync(await KeyAsync(projectId, false, cancellationToken), ModuleMergeStatus.Suggested, 50, cancellationToken));
        merges.AddRange(await merger.ListAsync(MemoryLabels.KnowledgeKey, ModuleMergeStatus.Suggested, 50, cancellationToken));

        var labeled = new List<MemoryItemDto>();
        for (var page = 1; page <= 10; page++)
        {
            var result = await memoryService.QueryAsync(new MemoryQuery(null, null, projectId, null, page, 200, Labeled: true), cancellationToken);
            labeled.AddRange(result.Items);
            if (labeled.Count >= result.Total) break;
        }
        // Pairs only within one project (or within knowledge memory): two projects may both have a module "User".
        var byId = labeled.ToDictionary(m => m.Id);
        var duplicates = labeled
            .GroupBy(m => m.Type == MemoryType.Knowledge ? MemoryLabels.KnowledgeKey : m.ProjectKey ?? m.ProjectId?.ToString() ?? string.Empty)
            .SelectMany(g => MemoryRanker.FindDuplicates(g.Select(m =>
                new MemoryIndexEntry(m.Id, m.Level, m.Type, m.Label, m.CreatedAt, m.Module, m.Layers, m.Kind, m.Area, m.Keywords)).ToList()))
            .OrderByDescending(d => d.Similarity)
            .Take(50)
            .Select(d => new MemoryDuplicateDto(byId[d.First.Id], byId[d.Second.Id], Math.Round(d.Similarity, 2)))
            .ToList();

        return new MemoryReviewDto(memories, merges, duplicates);
    }

    public Task<MemoryRelabelStatusDto> GetRelabelStatusAsync(CancellationToken cancellationToken = default) => relabeler.GetStatusAsync(cancellationToken);

    public void StartRelabel() =>
        _ = Task.Run(() => relabeler.RunAsync(false, lifetime.ApplicationStopping), CancellationToken.None);

    private async Task<string> KeyAsync(Guid? projectId, bool knowledge, CancellationToken cancellationToken)
    {
        if (knowledge) return MemoryLabels.KnowledgeKey;
        if (projectId is not { } id) throw new AppValidationException("Pilih project dulu.");
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var key = await db.Projects.AsNoTracking().Where(p => p.Id == id).Select(p => p.ProjectKey).FirstOrDefaultAsync(cancellationToken);
        if (key is null) throw new AppValidationException("Project tidak ditemukan.");
        if (key.Length == 0) throw new AppValidationException("Project belum punya key; buka lagi menu Project sebentar lagi.");
        return key;
    }

    private async Task<ProjectContext> ContextAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await projectService.GetContextAsync(projectId, cancellationToken) ?? throw new AppValidationException("Project tidak ditemukan.");
        if (string.IsNullOrEmpty(project.Key)) throw new AppValidationException("Project belum punya key; buka lagi menu Project sebentar lagi.");
        return project;
    }

    private async Task<string> DeciderAsync(CancellationToken cancellationToken) =>
        (await settingService.GetRuntimeAsync(cancellationToken)).EffectiveDeveloperName;

    private async Task<IReadOnlyList<ShortMemoryRecord>> ShortAsync()
    {
        if (!shortStore.IsConnected) return [];
        try
        {
            return await shortStore.GetAllAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read short memories.");
            return [];
        }
    }

    private static CodeMapEntryDto ToDto(CodeMapEntry e) => new(e.Id, e.Path, e.Module, e.Layer, e.Area, e.Source, e.UpdatedAt ?? e.CreatedAt);
}
