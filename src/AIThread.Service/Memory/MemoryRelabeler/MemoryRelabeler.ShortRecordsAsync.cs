using AIThread.Data.Cache;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Memory;

public sealed partial class MemoryRelabeler
{
    private async Task<IReadOnlyList<ShortMemoryRecord>> ShortRecordsAsync()
    {
        if (!shortStore.IsConnected) return [];
        try
        {
            return await shortStore.GetAllAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read short memories for relabeling.");
            return [];
        }
    }
}
