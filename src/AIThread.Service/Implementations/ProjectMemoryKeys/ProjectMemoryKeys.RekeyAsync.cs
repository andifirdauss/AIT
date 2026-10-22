using AIThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectMemoryKeys
{
    // A project whose key changed from its machine-only fallback (a remote, an override file or a manual key was
    // added) brings that memory along. A shared key is never rewritten: other developers' clones still use it.
    public async Task<int> RekeyAsync(string oldKey, string newKey, string projectName, CancellationToken cancellationToken)
    {
        if (oldKey == newKey || !ProjectKeyResolver.IsLocal(oldKey)) return 0;

        var updated = 0;
        try
        {
            await using var db = await longStore.CreateAsync(cancellationToken);
            updated += await db.LongMemories
                .Where(m => m.ProjectKey == oldKey)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProjectKey, newKey).SetProperty(m => m.ProjectName, projectName), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not move long memories from {OldKey} to {NewKey}.", oldKey, newKey);
        }

        updated += await UpdateShortAsync(r => r.ProjectKey == oldKey, r => r with { ProjectKey = newKey, ProjectName = projectName });
        await RekeyCatalogAsync(oldKey, newKey, cancellationToken);
        await RekeyUnitTestsAsync(oldKey, newKey, cancellationToken);
        return updated;
    }
}
