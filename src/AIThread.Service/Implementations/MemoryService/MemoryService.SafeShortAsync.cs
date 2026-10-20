using AIThread.Data.Cache;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryService
{
    private async Task<IReadOnlyList<ShortMemoryRecord>> SafeShortAsync()
    {
        if (!shortStore.IsConnected) return [];
        try
        {
            return await shortStore.GetAllAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read short memories from {Store}.", shortStore.Description);
            return [];
        }
    }
}
