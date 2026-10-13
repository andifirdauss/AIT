using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Data.Context;
using AiThread.Data.Memory;
using AiThread.Data.Messaging;
using AiThread.Service.Business;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed class BusinessMemoryService(
    IDbContextFactory<AiThreadDbContext> dbFactory,
    BusinessMemoryStore store,
    BusinessPageStore pages,
    BusinessDigestRunner runner,
    ISettingService settingService,
    IEventBus eventBus,
    IActivityLogService activityLog) : IBusinessMemoryService
{
    private const int PlannerContentMaxChars = 15_000;

    public async Task<BusinessOverviewDto?> GetOverviewAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await ProjectAsync(projectId, cancellationToken);
        if (project is null) return null;

        var key = project.ProjectKey;
        List<BusinessPage> rows;
        await using (var db = await store.CreateAsync(cancellationToken))
            rows = await SummaryQuery(db, key).ToListAsync(cancellationToken);

        // Threads of every clone of the project registered on this machine.
        int pending;
        DateTime? lastDigest;
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            var threads = db.Threads.AsNoTracking().Where(t => t.Project != null && t.Project.ProjectKey == key);
            pending = await threads.CountAsync(t => t.Status == ThreadStatus.Completed && t.BusinessDigestedAt == null, cancellationToken);
            lastDigest = await threads.MaxAsync(t => t.BusinessDigestedAt, cancellationToken);
        }

        return new BusinessOverviewDto(
            project.Id,
            key,
            project.Name,
            !ProjectKeyResolver.IsLocal(key),
            rows.OrderBy(p => p.Module).ThenBy(p => p.Title).Select(ToSummary).ToList(),
            pending,
            lastDigest,
            runner.IsRunning(key),
            store.Description);
    }

    public async Task<BusinessPageDto?> GetPageAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await store.CreateAsync(cancellationToken);
        var page = await db.BusinessPages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        return page is null ? null : ToDto(page);
    }

    public async Task<BusinessPageDto> CreatePageAsync(SaveBusinessPageRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        var project = await ProjectAsync(request.ProjectId, cancellationToken)
                      ?? throw new AppValidationException("Project tidak ditemukan.");
        var settings = await settingService.GetRuntimeAsync(cancellationToken);

        var page = await pages.SaveAsync(new BusinessPageDraft(
            null, project.ProjectKey, project.Name, request.Module, request.Title, request.Summary, request.Labels ?? string.Empty,
            request.Content, request.AutoUpdate, null, BusinessEditKind.Manual, settings.EffectiveDeveloperName,
            request.ChangeNote ?? "Dibuat manual", []), cancellationToken);

        Publish("created", page);
        activityLog.Info("business", $"Halaman bisnis '{page.Module} — {page.Title}' dibuat oleh {page.UpdatedBy}.", projectId: project.Id);
        return ToDto(page);
    }

    public async Task<BusinessPageDto?> UpdatePageAsync(Guid id, SaveBusinessPageRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        if (request.Version is null) throw new AppValidationException("Versi halaman wajib dikirim agar perubahan orang lain tidak tertimpa.");

        BusinessPage? current;
        await using (var db = await store.CreateAsync(cancellationToken))
            current = await db.BusinessPages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (current is null) return null;

        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        var page = await pages.SaveAsync(new BusinessPageDraft(
            id, current.ProjectKey, current.ProjectName, request.Module, request.Title, request.Summary, request.Labels ?? current.Labels,
            request.Content, request.AutoUpdate, request.Version, BusinessEditKind.Manual, settings.EffectiveDeveloperName,
            request.ChangeNote ?? "Diedit manual", []), cancellationToken);

        Publish("updated", page);
        activityLog.Info("business", $"Halaman bisnis '{page.Module} — {page.Title}' diedit oleh {page.UpdatedBy} (versi {page.Version}).");
        return ToDto(page);
    }

    public async Task<bool> DeletePageAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await store.CreateAsync(cancellationToken);
        var page = await db.BusinessPages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (page is null) return false;

        await db.BusinessPageRevisions.Where(r => r.PageId == id).ExecuteDeleteAsync(cancellationToken);
        await db.BusinessPages.Where(p => p.Id == id).ExecuteDeleteAsync(cancellationToken);

        // Images of the page go with it, unless another page of the project shows them too.
        var key = page.ProjectKey;
        var assets = await db.BusinessAssets.AsNoTracking().Where(a => a.PageId == id).Select(a => a.Id).ToListAsync(cancellationToken);
        foreach (var assetId in assets)
        {
            var reference = BusinessMarkdown.AssetScheme + assetId;
            var used = await db.BusinessPages.AnyAsync(p => p.ProjectKey == key && p.Content.Contains(reference), cancellationToken);
            if (used) await db.BusinessAssets.Where(a => a.Id == assetId).ExecuteUpdateAsync(s => s.SetProperty(a => a.PageId, (Guid?)null), cancellationToken);
            else await db.BusinessAssets.Where(a => a.Id == assetId).ExecuteDeleteAsync(cancellationToken);
        }

        Publish("deleted", page);
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        activityLog.Info("business", $"Halaman bisnis '{page.Module} — {page.Title}' dihapus oleh {settings.EffectiveDeveloperName}.");
        return true;
    }

    public async Task<IReadOnlyList<BusinessRevisionDto>> GetRevisionsAsync(Guid pageId, CancellationToken cancellationToken = default)
    {
        await using var db = await store.CreateAsync(cancellationToken);
        var rows = await db.BusinessPageRevisions.AsNoTracking()
            .Where(r => r.PageId == pageId)
            .OrderByDescending(r => r.Version)
            .Select(r => new { r.Id, r.Version, r.Title, r.ChangeNote, r.Author, r.EditKind, r.CreatedAt, r.SourceThreadIds })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new BusinessRevisionDto(r.Id, r.Version, r.Title, r.ChangeNote, r.Author, r.EditKind, r.CreatedAt, ThreadIds(r.SourceThreadIds))).ToList();
    }

    public async Task<BusinessRevisionDto?> GetRevisionAsync(Guid pageId, int version, CancellationToken cancellationToken = default)
    {
        await using var db = await store.CreateAsync(cancellationToken);
        var r = await db.BusinessPageRevisions.AsNoTracking().FirstOrDefaultAsync(r => r.PageId == pageId && r.Version == version, cancellationToken);
        return r is null ? null : new BusinessRevisionDto(r.Id, r.Version, r.Title, r.ChangeNote, r.Author, r.EditKind, r.CreatedAt, ThreadIds(r.SourceThreadIds), r.Content);
    }

    public async Task<BusinessPageDto?> RestoreAsync(Guid pageId, int version, CancellationToken cancellationToken = default)
    {
        BusinessPage? current;
        BusinessPageRevision? revision;
        await using (var db = await store.CreateAsync(cancellationToken))
        {
            current = await db.BusinessPages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pageId, cancellationToken);
            revision = await db.BusinessPageRevisions.AsNoTracking().FirstOrDefaultAsync(r => r.PageId == pageId && r.Version == version, cancellationToken);
        }
        if (current is null || revision is null) return null;

        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        var page = await pages.SaveAsync(new BusinessPageDraft(
            pageId, current.ProjectKey, current.ProjectName, current.Module, revision.Title, current.Summary, current.Labels,
            revision.Content, current.AutoUpdate, current.Version, BusinessEditKind.Restore, settings.EffectiveDeveloperName,
            $"Dipulihkan dari versi {version}", []), cancellationToken);

        Publish("updated", page);
        activityLog.Info("business", $"Halaman bisnis '{page.Module} — {page.Title}' dipulihkan ke versi {version} oleh {page.UpdatedBy}.");
        return ToDto(page);
    }

    public async Task<BusinessAssetDto> AddAssetAsync(Guid projectId, Guid? pageId, byte[] data, string? caption, CancellationToken cancellationToken = default)
    {
        var project = await ProjectAsync(projectId, cancellationToken) ?? throw new AppValidationException("Project tidak ditemukan.");
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        var asset = await pages.AddAssetAsync(project.ProjectKey, pageId, data, caption, null, settings.EffectiveDeveloperName, cancellationToken);
        return new BusinessAssetDto(asset.Id, asset.MediaType, asset.Caption, BusinessMarkdown.AssetRoute + asset.Id, data.Length);
    }

    public async Task<(string MediaType, byte[] Data)?> GetAssetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await store.CreateAsync(cancellationToken);
        var asset = await db.BusinessAssets.AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new { a.MediaType, a.Data })
            .FirstOrDefaultAsync(cancellationToken);
        return asset is null ? null : (asset.MediaType, asset.Data);
    }

    public string Render(string markdown) => BusinessMarkdown.ToHtml(markdown ?? string.Empty);

    public async Task<IReadOnlyList<BusinessPageSummaryDto>> GetPagesAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await ProjectAsync(projectId, cancellationToken);
        if (project is null || string.IsNullOrEmpty(project.ProjectKey)) return [];

        await using var db = await store.CreateAsync(cancellationToken);
        return (await SummaryQuery(db, project.ProjectKey).ToListAsync(cancellationToken)).Select(ToSummary).ToList();
    }

    public async Task<IReadOnlyList<(BusinessPageSummaryDto Page, string Content)>> GetForPlannerAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.Distinct().Take(20).ToList();
        if (idList.Count == 0) return [];

        await using var db = await store.CreateAsync(cancellationToken);
        var rows = await db.BusinessPages.AsNoTracking().Where(p => idList.Contains(p.Id)).ToListAsync(cancellationToken);
        return rows.Select(p =>
        {
            var content = BusinessMarkdown.ForPlanner(p.Content);
            if (content.Length > PlannerContentMaxChars) content = content[..PlannerContentMaxChars] + "\n… (page truncated)";
            return (ToSummary(p), content);
        }).ToList();
    }

    public async Task<BusinessSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default) =>
        ToSettingsDto(await settingService.GetRuntimeAsync(cancellationToken));

    public async Task<BusinessSettingsDto> UpdateSettingsAsync(UpdateBusinessSettingsRequest request, CancellationToken cancellationToken = default)
    {
        var settings = await settingService.UpdateBusinessAsync(request, cancellationToken);
        activityLog.Info("business",
            $"Setting business memory disimpan: {(settings.BusinessMemoryEnabled ? $"aktif tiap {settings.BusinessMemoryIntervalHours} jam" : "nonaktif")}, model {settings.BusinessMemoryModel ?? "otomatis — node Planner"}, developer {settings.EffectiveDeveloperName}.");
        eventBus.Publish(AppEvent.Create(AppEventTypes.BusinessChanged, new { action = "settings" }));
        return ToSettingsDto(settings);
    }

    public async Task<BusinessSettingsDto> SetDeveloperNameAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new AppValidationException("Nama developer wajib diisi.");
        var current = await settingService.GetRuntimeAsync(cancellationToken);
        var settings = await settingService.UpdateBusinessAsync(new UpdateBusinessSettingsRequest(
            current.BusinessMemoryEnabled, current.BusinessMemoryIntervalHours, current.BusinessMemoryModel, name), cancellationToken);
        activityLog.Info("business", $"Nama developer di AIT ini: {settings.EffectiveDeveloperName}.");
        eventBus.Publish(AppEvent.Create(AppEventTypes.BusinessChanged, new { action = "settings" }));
        return ToSettingsDto(settings);
    }

    // Summary columns only: Content may be large.
    private static IQueryable<BusinessPage> SummaryQuery(IBusinessMemoryDb db, string key) =>
        db.BusinessPages.AsNoTracking()
            .Where(p => p.ProjectKey == key)
            .Select(p => new BusinessPage
            {
                Id = p.Id, ProjectKey = p.ProjectKey, ProjectName = p.ProjectName, Module = p.Module, Title = p.Title, Slug = p.Slug,
                Summary = p.Summary, Labels = p.Labels, Version = p.Version, AutoUpdate = p.AutoUpdate, CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt, UpdatedBy = p.UpdatedBy, LastEditKind = p.LastEditKind
            });

    private async Task<Project?> ProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId && p.ProjectKey != "", cancellationToken);
    }

    private void Publish(string action, BusinessPage page) =>
        eventBus.Publish(AppEvent.Create(AppEventTypes.BusinessChanged, new { action, id = page.Id, projectKey = page.ProjectKey }));

    private static BusinessPageSummaryDto ToSummary(BusinessPage p) => new(
        p.Id, p.ProjectKey, p.ProjectName, p.Module, p.Title, p.Slug, p.Summary, p.Labels, p.Version, p.AutoUpdate,
        p.CreatedAt, p.UpdatedAt, p.UpdatedBy, p.LastEditKind);

    private static BusinessPageDto ToDto(BusinessPage p) => new(ToSummary(p), p.Content, BusinessMarkdown.ToHtml(p.Content));

    private static BusinessSettingsDto ToSettingsDto(RuntimeSettings s) => new(
        s.BusinessMemoryEnabled, s.BusinessMemoryIntervalHours, s.BusinessMemoryModel, s.DeveloperName, s.EffectiveDeveloperName,
        GitIdentity.UserName);

    private static IReadOnlyList<Guid> ThreadIds(string? csv) =>
        string.IsNullOrEmpty(csv) ? [] : csv.Split(',').Select(s => Guid.TryParse(s, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToList();

    private static void Validate(SaveBusinessPageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Module)) throw new AppValidationException("Modul wajib diisi (mis. TO).");
        if (request.Module.Trim().Length > 100) throw new AppValidationException("Modul maksimal 100 karakter.");
        if (string.IsNullOrWhiteSpace(request.Title)) throw new AppValidationException("Judul wajib diisi.");
        if (request.Title.Trim().Length > 200) throw new AppValidationException("Judul maksimal 200 karakter.");
        if (request.Summary?.Length > 1000) throw new AppValidationException("Ringkasan maksimal 1.000 karakter.");
        if (request.Labels?.Length > 500) throw new AppValidationException("Label maksimal 500 karakter.");
        if (string.IsNullOrWhiteSpace(request.Content)) throw new AppValidationException("Isi halaman wajib diisi.");
        if (request.Content.Length > BusinessPageStore.MaxContentChars)
            throw new AppValidationException($"Isi halaman maksimal {BusinessPageStore.MaxContentChars:N0} karakter.");
    }
}
