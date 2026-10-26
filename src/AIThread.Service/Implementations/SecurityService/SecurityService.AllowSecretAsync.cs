using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Security;

namespace AIThread.Service.Implementations;

public sealed partial class SecurityService
{
    public async Task<bool> AllowSecretAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await LoadTrackedAsync(db, cancellationToken);
        var entries = SecretAllowList.Parse(entity.SecretAllowListJson);
        if (entries.Any(e => e.Id == id)) return true;
        if (secrets.GetSightings().FirstOrDefault(s => s.Id == id) is not { } seen) return false;

        entries.Add(new SecretAllowEntry(id, seen.Label, seen.Preview, seen.Length, DateTime.UtcNow));
        entity.SecretAllowListJson = SecretAllowList.Serialize(entries);
        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        secrets.SetAllowed(entries.Select(e => e.Id));
        activityLog.Info("keamanan", Lang.T($"Nilai {seen.Preview} ({seen.Label}) ditandai bukan secret dan tidak disamarkan lagi di mesin ini.",
            $"The value {seen.Preview} ({seen.Label}) is marked as not secret and no longer masked on this machine."));
        return true;
    }
}
