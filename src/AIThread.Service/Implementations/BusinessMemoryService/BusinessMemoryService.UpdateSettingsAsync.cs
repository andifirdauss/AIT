using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Implementations;

public sealed partial class BusinessMemoryService
{
    public async Task<BusinessSettingsDto> UpdateSettingsAsync(UpdateBusinessSettingsRequest request, CancellationToken cancellationToken = default)
    {
        var settings = await settingService.UpdateBusinessAsync(request, cancellationToken);
        activityLog.Info("business",
            Lang.T($"Pengaturan business memory disimpan: {(settings.BusinessMemoryEnabled ? $"aktif tiap {settings.BusinessMemoryIntervalHours} jam" : "nonaktif")}, model {settings.BusinessMemoryModel ?? "otomatis — node Planner"}, developer {settings.EffectiveDeveloperName}.", $"Business memory settings saved: {(settings.BusinessMemoryEnabled ? $"on every {settings.BusinessMemoryIntervalHours} hours" : "off")}, model {settings.BusinessMemoryModel ?? "automatic — Planner node"}, developer {settings.EffectiveDeveloperName}."));
        eventBus.Publish(AppEvent.Create(AppEventTypes.BusinessChanged, new { action = "settings" }));
        return ToSettingsDto(settings);
    }
}
