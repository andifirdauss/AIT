using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Data.Memory;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectMemoryKeys
{
    // Fills ProjectKey/ProjectName of rows written before keys existed. Idempotent; every developer's AIThread runs
    // it for its own projects, so a shared store ends up fully keyed.
    public async Task<int> BackfillAsync(IReadOnlyList<Project> projects, CancellationToken cancellationToken)
    {
        var keyed = projects.Where(p => !string.IsNullOrEmpty(p.ProjectKey)).ToDictionary(p => p.Id);
        if (keyed.Count == 0) return 0;

        var updated = 0;
        try
        {
            await using var db = await longStore.CreateAsync(cancellationToken);
            foreach (var project in keyed.Values)
            {
                var key = project.ProjectKey;
                var name = project.Name;
                var id = project.Id;
                updated += await db.LongMemories
                    .Where(m => m.ProjectKey == null && m.ProjectId == id && m.Type != MemoryType.Knowledge)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProjectKey, key).SetProperty(m => m.ProjectName, name), cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogStoreWarning(ex, "Could not fill the project keys of long memories");
        }

        updated += await UpdateShortAsync(
            r => r.ProjectKey is null && r.Type != MemoryType.Knowledge && r.ProjectId is { } id && keyed.ContainsKey(id),
            r => r with { ProjectKey = keyed[r.ProjectId!.Value].ProjectKey, ProjectName = keyed[r.ProjectId!.Value].Name });
        return updated;
    }
}
