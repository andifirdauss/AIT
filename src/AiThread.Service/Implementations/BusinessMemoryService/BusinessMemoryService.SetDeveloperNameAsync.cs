using AiThread.Common.DTOs;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;

namespace AiThread.Service.Implementations;

public sealed partial class BusinessMemoryService
{
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
}
