using AiThread.Service.Interfaces;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryService
{
    public async Task<bool> PromoteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        EnsureShortStore();
        var record = await shortStore.GetAsync(id);
        if (record is null) return false;

        await InsertLongAsync(record, cancellationToken);
        await shortStore.DeleteAsync(id);
        Publish("promoted", id);
        activityLog.Info("memory", $"Memory '{record.Label}' dipindah ke long memory secara manual.", record.SourceThreadId, record.ProjectId);
        return true;
    }
}
