using AIThread.Service.Models;
using Microsoft.AspNetCore.DataProtection;

namespace AIThread.Service.Implementations;

public sealed partial class SettingService
{
    public async Task<RuntimeSettings> UpdateRouterAsync(string baseUrl, string? apiKey, CancellationToken cancellationToken = default)
    {
        var url = ValidateRouterUrl(baseUrl);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await LoadTrackedAsync(db, cancellationToken);

        entity.NineRouterBaseUrl = url;
        if (!string.IsNullOrWhiteSpace(apiKey)) entity.NineRouterApiKeyProtected = _protector.Protect(apiKey.Trim());
        entity.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        _cache = ToRuntime(entity);
        return _cache;
    }
}
