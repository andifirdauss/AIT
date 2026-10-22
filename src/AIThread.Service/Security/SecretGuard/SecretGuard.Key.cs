namespace AIThread.Service.Security;

public sealed partial class SecretGuard
{
    // The HMAC key, read or created once (LoadKey).
    private byte[] Key()
    {
        if (_key is { } key) return key;
        lock (_keyLock) return _key ??= LoadKey();
    }
}
