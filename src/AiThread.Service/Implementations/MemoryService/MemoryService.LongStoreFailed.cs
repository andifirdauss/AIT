using AiThread.Data.Memory;
using AiThread.Service.Interfaces;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryService
{
    private void LongStoreFailed(Exception ex, string what)
    {
        logger.LogStoreWarning(ex, what);
        if (!StoreErrors.IsUnreachable(ex)) return;

        var now = DateTime.UtcNow.Ticks;
        var last = Interlocked.Read(ref _longStoreNoticeTicks);
        if (now - last < LongStoreNoticeInterval.Ticks || Interlocked.CompareExchange(ref _longStoreNoticeTicks, now, last) != last) return;
        activityLog.Warn("memory", $"Long memory ({longStore.Description}) tidak dapat dihubungi: {MemoryStoreTester.ErrorMessage(ex)}. Thread tetap berjalan tanpa long memory.");
    }
}
