using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class SettingService
{
    public async Task<RuntimeSettings> UpdateBusinessAsync(UpdateBusinessSettingsRequest request, CancellationToken cancellationToken = default)
    {
        EnsureRange(request.IntervalHours, 1, 168, Lang.T("Interval business memory (jam)", "Business memory interval (hours)"));
        if (request.DeveloperName?.Trim().Length > 200) throw new AppValidationException(Lang.T("Nama developer maksimal 200 karakter.", "The developer name is at most 200 characters."));
        if (request.Model?.Trim().Length > 200) throw new AppValidationException(Lang.T("Model maksimal 200 karakter.", "The model is at most 200 characters."));

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await LoadTrackedAsync(db, cancellationToken);
        entity.BusinessMemoryEnabled = request.Enabled;
        entity.BusinessMemoryIntervalHours = request.IntervalHours;
        entity.BusinessMemoryModel = string.IsNullOrWhiteSpace(request.Model) ? null : request.Model.Trim();
        entity.DeveloperName = string.IsNullOrWhiteSpace(request.DeveloperName) ? null : request.DeveloperName.Trim();
        entity.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        _cache = ToRuntime(entity);
        return _cache;
    }
}
