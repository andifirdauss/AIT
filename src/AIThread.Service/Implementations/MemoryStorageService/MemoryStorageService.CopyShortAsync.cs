using AIThread.Data.Cache;
using AIThread.Data.Memory;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryStorageService
{
    // Upserts by id, so copying twice (or back and forth) never duplicates.
    private async Task<int> CopyShortAsync(MemoryStoreConnection from, IShortMemoryStore to, List<string> warnings)
    {
        var source = from == shortStore.Connection ? shortStore : shortStore.Open(from);
        try
        {
            var records = await source.GetAllAsync();
            foreach (var record in records) await to.SaveAsync(record);
            return records.Count;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Short memories could not be copied to the new store.");
            warnings.Add($"Short memory lama tidak dapat disalin: {MemoryStoreTester.ErrorMessage(ex)}");
            return 0;
        }
        finally
        {
            if (!ReferenceEquals(source, shortStore)) (source as IDisposable)?.Dispose();
        }
    }
}
