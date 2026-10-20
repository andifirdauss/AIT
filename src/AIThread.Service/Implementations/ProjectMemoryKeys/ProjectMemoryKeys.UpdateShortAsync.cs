using AIThread.Data.Cache;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectMemoryKeys
{
    private async Task<int> UpdateShortAsync(Func<ShortMemoryRecord, bool> match, Func<ShortMemoryRecord, ShortMemoryRecord> update)
    {
        if (!shortStore.IsConnected) return 0;
        try
        {
            var records = (await shortStore.GetAllAsync()).Where(match).ToList();
            foreach (var record in records) await shortStore.SaveAsync(update(record));
            return records.Count;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not update the project keys of short memories.");
            return 0;
        }
    }
}
