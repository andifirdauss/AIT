using AiThread.Common.DTOs;
using AiThread.Service.Interfaces;

namespace AiThread.Service.Implementations;

public sealed partial class BusinessMemoryService
{
    public async Task<BusinessSettingsDto> UpdateSettingsAsync(UpdateBusinessSettingsRequest request, CancellationToken cancellationToken = default)
    {
        var settings = await settingService.UpdateBusinessAsync(request, cancellationToken);
        activityLog.Info("business",
            $"Setting business memory disimpan: {(settings.BusinessMemoryEnabled ? $"aktif tiap {settings.BusinessMemoryIntervalHours} jam" : "nonaktif")}, model {settings.BusinessMemoryModel ?? "otomatis — node Planner"}, developer {settings.EffectiveDeveloperName}.");
        eventBus.Publish(AppEvent.Create(AppEventTypes.BusinessChanged, new { action = "settings" }));
        return ToSettingsDto(settings);
    }
}
