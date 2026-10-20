using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Service.Business;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class BusinessMemoryService
{
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
}
