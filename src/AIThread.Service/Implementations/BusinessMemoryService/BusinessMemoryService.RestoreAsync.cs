using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Business;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class BusinessMemoryService
{
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
            Lang.T($"Dipulihkan dari versi {version}", $"Restored from version {version}"), []), cancellationToken);

        Publish("updated", page);
        activityLog.Info("business", Lang.T($"Halaman bisnis '{page.Module} — {page.Title}' dipulihkan ke versi {version} oleh {page.UpdatedBy}.", $"Business page '{page.Module} — {page.Title}' restored to version {version} by {page.UpdatedBy}."));
        return ToDto(page);
    }
}
