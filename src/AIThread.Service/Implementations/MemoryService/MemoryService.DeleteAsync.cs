using AIThread.Common.Enums;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryService
{
    public async Task<bool> DeleteAsync(MemoryLevel level, Guid id, CancellationToken cancellationToken = default)
    {
        bool deleted;
        if (level == MemoryLevel.Short)
        {
            EnsureShortStore();
            deleted = await shortStore.DeleteAsync(id);
        }
        else
        {
            await using var db = await longStore.CreateAsync(cancellationToken);
            deleted = await db.LongMemories.Where(m => m.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;
        }

        if (deleted) Publish("deleted", id);
        return deleted;
    }
}
