using AIThread.Common.Localization;
using AIThread.Data.Memory;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryService
{
    private void LongStoreFailed(Exception ex, string what)
    {
        logger.LogStoreWarning(ex, what);
        if (!StoreErrors.IsUnreachable(ex)) return;

        var now = DateTime.UtcNow.Ticks;
        var last = Interlocked.Read(ref _longStoreNoticeTicks);
        if (now - last < LongStoreNoticeInterval.Ticks || Interlocked.CompareExchange(ref _longStoreNoticeTicks, now, last) != last) return;
        activityLog.Warn("memory", Lang.T($"Long memory ({longStore.Description}) tidak dapat dihubungi: {MemoryStoreTester.ErrorMessage(ex)}. Thread tetap berjalan tanpa long memory.", $"Long memory ({longStore.Description}) cannot be reached: {MemoryStoreTester.ErrorMessage(ex)}. The thread continues without long memory."));
    }
}
