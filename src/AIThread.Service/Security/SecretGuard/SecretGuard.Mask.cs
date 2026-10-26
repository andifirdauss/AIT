namespace AIThread.Service.Security;

public sealed partial class SecretGuard
{
    // The placeholder of a value, remembered so it can be written back. count: masked on its way out (a learned file
    // is not). A value marked as not secret stays as it is.
    private string Mask(string label, string value, bool count)
    {
        var id = Id(value);
        if (_values.Count < MaxVaultEntries || _values.ContainsKey(id)) _values.TryAdd(id, value);
        if (_allowed.Contains(id)) return value;
        if (SecretRules.IsDistinctive(value) && _distinctive.Count < MaxDistinctive && _distinctive.TryAdd(value, label))
            Interlocked.Increment(ref _distinctiveVersion);
        if (count)
        {
            Interlocked.Increment(ref _masked);
            _tally.Value?.Add(id);
            if (_sightings.Count < MaxVaultEntries || _sightings.ContainsKey(id))
                _sightings.GetOrAdd(id, _ => new Sighting(label, value.Length)).Seen();
        }
        return SecretRules.Placeholder(label, id);
    }
}
