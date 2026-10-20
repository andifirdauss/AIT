using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Service.Business;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class BusinessMemoryService
{
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
}
