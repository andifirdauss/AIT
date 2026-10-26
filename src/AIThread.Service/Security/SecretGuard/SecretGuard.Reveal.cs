namespace AIThread.Service.Security;

public sealed partial class SecretGuard
{
    // The real value behind a placeholder id, for the developer deciding whether it is a secret (loopback callers only,
    // SecurityController). Null when the vault does not hold it (AIT restarted since).
    public string? Reveal(string id) => _values.TryGetValue(id, out var value) ? value : null;
}
