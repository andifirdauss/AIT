using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class BusinessMemoryService
{
    public async Task<BusinessSettingsDto> SetDeveloperNameAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new AppValidationException(Lang.T("Nama developer wajib diisi.", "The developer name is required."));
        var current = await settingService.GetRuntimeAsync(cancellationToken);
        var settings = await settingService.UpdateBusinessAsync(new UpdateBusinessSettingsRequest(
            current.BusinessMemoryEnabled, current.BusinessMemoryIntervalHours, current.BusinessMemoryModel, name), cancellationToken);
        activityLog.Info("business", Lang.T($"Nama developer di AIT ini: {settings.EffectiveDeveloperName}.", $"Developer name on this AIT: {settings.EffectiveDeveloperName}."));
        eventBus.Publish(AppEvent.Create(AppEventTypes.BusinessChanged, new { action = "settings" }));
        return ToSettingsDto(settings);
    }
}
