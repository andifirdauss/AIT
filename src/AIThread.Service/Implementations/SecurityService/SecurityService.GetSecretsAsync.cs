using AIThread.Common.DTOs;
using AIThread.Service.Security;

namespace AIThread.Service.Implementations;

public sealed partial class SecurityService
{
    private const int MaxListedSecrets = 300;

    public async Task<DetectedSecretsDto> GetSecretsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var allowed = SecretAllowList.Parse((await LoadTrackedAsync(db, cancellationToken)).SecretAllowListJson).ToDictionary(e => e.Id, StringComparer.Ordinal);
        var sightings = secrets.GetSightings();

        // Marked values first (also after a restart, when only the stored entry is left), then the latest masked ones.
        var items = allowed.Values
            .OrderByDescending(e => e.AllowedAt)
            .Select(e => sightings.FirstOrDefault(s => s.Id == e.Id) is { } seen
                ? new DetectedSecretDto(e.Id, e.Label, e.Preview, e.Length, seen.Count, seen.FirstSeen, seen.LastSeen, true, e.AllowedAt)
                : new DetectedSecretDto(e.Id, e.Label, e.Preview, e.Length, 0, null, null, true, e.AllowedAt))
            .Concat(sightings
                .Where(s => !allowed.ContainsKey(s.Id))
                .Select(s => new DetectedSecretDto(s.Id, s.Label, s.Preview, s.Length, s.Count, s.FirstSeen, s.LastSeen, false, null)))
            .ToList();
        return new DetectedSecretsDto(items.Take(MaxListedSecrets).ToList(), items.Count);
    }
}
