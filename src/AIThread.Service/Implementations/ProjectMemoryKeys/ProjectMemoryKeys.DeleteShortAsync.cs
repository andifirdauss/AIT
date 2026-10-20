using AIThread.Data.Cache;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectMemoryKeys
{
    private async Task<int> DeleteShortAsync(Func<ShortMemoryRecord, bool> match)
    {
        if (!shortStore.IsConnected) return 0;
        try
        {
            var records = (await shortStore.GetAllAsync()).Where(match).ToList();
            foreach (var record in records) await shortStore.DeleteAsync(record.Id);
            return records.Count;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not delete short memories.");
            return 0;
        }
    }
}
