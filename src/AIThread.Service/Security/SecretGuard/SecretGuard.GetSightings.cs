namespace AIThread.Service.Security;

public sealed partial class SecretGuard
{
    // The values masked on their way out since AIT started, most recent first, with a preview instead of the value.
    public IReadOnlyList<SecretSighting> GetSightings() =>
        _sightings
            .Select(p => new SecretSighting(p.Key, p.Value.Label, _values.TryGetValue(p.Key, out var value) ? Preview(value) : "…",
                p.Value.Length, p.Value.Count, p.Value.FirstSeen, p.Value.LastSeen))
            .OrderByDescending(s => s.LastSeen)
            .ToList();
}
