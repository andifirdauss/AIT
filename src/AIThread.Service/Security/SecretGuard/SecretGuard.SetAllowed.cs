namespace AIThread.Service.Security;

public sealed partial class SecretGuard
{
    // The placeholder ids of the values marked as not secret (SystemSetting.SecretAllowListJson): from now on they stay
    // as they are, also where they appear again. Placeholders already sent still come back.
    public void SetAllowed(IEnumerable<string> ids)
    {
        var next = new HashSet<string>(ids, StringComparer.Ordinal);
        if (next.SetEquals(_allowed)) return;
        _allowed = next;

        var removed = false;
        foreach (var id in next)
            if (_values.TryGetValue(id, out var value) && _distinctive.TryRemove(value, out _)) removed = true;
        if (removed) Interlocked.Increment(ref _distinctiveVersion);
    }
}
