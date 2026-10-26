using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Security;

namespace AIThread.Service.Implementations;

public sealed partial class SecurityService
{
    public async Task<bool> DisallowSecretAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await LoadTrackedAsync(db, cancellationToken);
        var entries = SecretAllowList.Parse(entity.SecretAllowListJson);
        if (entries.FirstOrDefault(e => e.Id == id) is not { } entry) return false;

        entries.Remove(entry);
        entity.SecretAllowListJson = SecretAllowList.Serialize(entries);
        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        secrets.SetAllowed(entries.Select(e => e.Id));
        activityLog.Info("keamanan", Lang.T($"Nilai {entry.Preview} ({entry.Label}) disamarkan lagi.", $"The value {entry.Preview} ({entry.Label}) is masked again."));
        return true;
    }
}
