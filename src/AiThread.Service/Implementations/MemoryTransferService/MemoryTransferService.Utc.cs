namespace AiThread.Service.Implementations;

public sealed partial class MemoryTransferService
{
    private static DateTime Utc(DateTime value) => value == default ? DateTime.UtcNow
        : value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
        : value.ToUniversalTime();
}
