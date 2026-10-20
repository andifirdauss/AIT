using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Service.Business;
using AiThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

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
            $"Dipulihkan dari versi {version}", []), cancellationToken);

        Publish("updated", page);
        activityLog.Info("business", $"Halaman bisnis '{page.Module} — {page.Title}' dipulihkan ke versi {version} oleh {page.UpdatedBy}.");
        return ToDto(page);
    }
}
