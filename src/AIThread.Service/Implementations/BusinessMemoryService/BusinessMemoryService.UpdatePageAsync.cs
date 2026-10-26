using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Business;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class BusinessMemoryService
{
    public async Task<BusinessPageDto?> UpdatePageAsync(Guid id, SaveBusinessPageRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        if (request.Version is null) throw new AppValidationException(Lang.T("Versi halaman wajib dikirim agar perubahan orang lain tidak tertimpa.", "The page version must be sent so other people's changes are not overwritten."));

        BusinessPage? current;
        await using (var db = await store.CreateAsync(cancellationToken))
            current = await db.BusinessPages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (current is null) return null;

        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        var page = await pages.SaveAsync(new BusinessPageDraft(
            id, current.ProjectKey, current.ProjectName, request.Module, request.Title, request.Summary, request.Labels ?? current.Labels,
            request.Content, request.AutoUpdate, request.Version, BusinessEditKind.Manual, settings.EffectiveDeveloperName,
            request.ChangeNote ?? Lang.T("Diedit manual", "Edited manually"), []), cancellationToken);

        Publish("updated", page);
        activityLog.Info("business", Lang.T($"Halaman bisnis '{page.Module} — {page.Title}' diedit oleh {page.UpdatedBy} (versi {page.Version}).", $"Business page '{page.Module} — {page.Title}' edited by {page.UpdatedBy} (version {page.Version})."));
        return ToDto(page);
    }
}
