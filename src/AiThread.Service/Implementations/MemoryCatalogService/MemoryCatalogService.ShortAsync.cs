using AiThread.Data.Cache;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryCatalogService
{
    private async Task<IReadOnlyList<ShortMemoryRecord>> ShortAsync()
    {
        if (!shortStore.IsConnected) return [];
        try
        {
            return await shortStore.GetAllAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read short memories.");
            return [];
        }
    }
}
