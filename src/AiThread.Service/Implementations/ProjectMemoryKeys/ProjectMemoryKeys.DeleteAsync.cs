using AiThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

public sealed partial class ProjectMemoryKeys
{
    // Removes the memory of a project being deleted. Memory in a shared store belongs to every developer of the
    // project and is kept unless includeShared; a machine-only key is never shared, wherever it is stored.
    public async Task<int> DeleteAsync(Guid projectId, string projectKey, bool includeShared, CancellationToken cancellationToken)
    {
        var localKey = string.IsNullOrEmpty(projectKey) || ProjectKeyResolver.IsLocal(projectKey);
        var key = string.IsNullOrEmpty(projectKey) ? null : projectKey;
        var deleted = 0;

        if (!ShortStoreIsShared || includeShared || localKey)
            deleted += await DeleteShortAsync(r => (key is not null && r.ProjectKey == key) || (r.ProjectKey is null && r.ProjectId == projectId));

        if (!LongStoreIsShared || includeShared || localKey)
        {
            try
            {
                await using var db = await longStore.CreateAsync(cancellationToken);
                deleted += await db.LongMemories
                    .Where(m => (key != null && m.ProjectKey == key) || (m.ProjectKey == null && m.ProjectId == projectId))
                    .ExecuteDeleteAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not delete long memories of project {ProjectId}", projectId);
            }

            // The module list and code map go with the memory they describe.
            if (key is not null)
            {
                try
                {
                    await using var catalog = await longStore.CreateCatalogAsync(cancellationToken);
                    await catalog.CodeMapEntries.Where(e => e.ProjectKey == key).ExecuteDeleteAsync(cancellationToken);
                    await catalog.ModuleMergeLogs.Where(l => l.ProjectKey == key).ExecuteDeleteAsync(cancellationToken);
                    await catalog.ProjectModules.Where(m => m.ProjectKey == key).ExecuteDeleteAsync(cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Could not delete the module list and code map of project {ProjectId}", projectId);
                }
            }
        }
        return deleted;
    }
}
