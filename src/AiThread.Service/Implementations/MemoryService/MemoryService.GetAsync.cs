using AiThread.Common.DTOs;
using AiThread.Common.Enums;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryService
{
    public async Task<MemoryItemDto?> GetAsync(MemoryLevel level, Guid id, CancellationToken cancellationToken = default)
    {
        var projects = await LocalProjectsAsync(cancellationToken);
        if (level == MemoryLevel.Short)
        {
            var record = shortStore.IsConnected ? await shortStore.GetAsync(id) : null;
            return record is null ? null : ToDto(record, projects);
        }

        await using var db = await longStore.CreateAsync(cancellationToken);
        var entity = await db.LongMemories.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        return entity is null ? null : ToDto(entity, projects);
    }
}
