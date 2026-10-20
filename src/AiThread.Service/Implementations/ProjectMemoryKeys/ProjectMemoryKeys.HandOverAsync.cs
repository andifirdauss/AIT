using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class ProjectMemoryKeys
{
    // Another clone of the same repository stays registered: long memory in the main database follows it instead of
    // being removed with the project by the foreign key.
    public async Task HandOverAsync(Guid fromProjectId, Guid toProjectId, CancellationToken cancellationToken)
    {
        if (LongStoreIsShared) return;
        await using var db = await longStore.CreateAsync(cancellationToken);
        await db.LongMemories
            .Where(m => m.ProjectId == fromProjectId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProjectId, toProjectId), cancellationToken);
    }
}
