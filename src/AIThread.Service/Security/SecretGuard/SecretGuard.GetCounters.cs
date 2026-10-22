namespace AIThread.Service.Security;

public sealed partial class SecretGuard
{
    public SecurityCounters GetCounters() =>
        new(Interlocked.Read(ref _masked), _values.Count, Interlocked.Read(ref _restored), SecretPaths.Blocked);
}
