using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class SettingService
{
    // Only touches the language; it applies at once (Lang.Set through ToRuntime), the web UI reloads to show it.
    public async Task<RuntimeSettings> UpdateLanguageAsync(string language, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await LoadTrackedAsync(db, cancellationToken);
        entity.UiLanguage = Lang.Normalize(language);
        entity.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        _cache = ToRuntime(entity);
        return _cache;
    }
}
