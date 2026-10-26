using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectService
{
    public async Task<bool> DeleteAsync(Guid id, bool includeSharedMemory = false, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (project is null) return false;

        // Another clone of the same repository is still registered here: the memory stays with it.
        var key = project.ProjectKey;
        var sibling = string.IsNullOrEmpty(key)
            ? null
            : await db.Projects.Where(p => p.Id != id && p.ProjectKey == key).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);
        if (sibling is { } siblingId)
        {
            try
            {
                await memoryKeys.HandOverAsync(id, siblingId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not hand the long memories of project {ProjectId} over to {SiblingId}", id, siblingId);
            }
        }

        db.Projects.Remove(project);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var (sessionKey, decision) in _sessions)
            if (decision.ProjectId == id) _sessions.TryRemove(sessionKey, out _);

        string outcome;
        if (sibling is not null)
        {
            outcome = Lang.T("memory-nya tetap dipakai clone lain dari repository yang sama", "its memory is still used by another clone of the same repository");
        }
        else
        {
            await memoryKeys.DeleteAsync(id, key, includeSharedMemory, cancellationToken);
            var sharedKept = !includeSharedMemory && !ProjectKeyResolver.IsLocal(key)
                             && (memoryKeys.LongStoreIsShared || memoryKeys.ShortStoreIsShared);
            outcome = sharedKept ? Lang.T("memory di store central tetap disimpan untuk developer lain", "the memory in the central store is kept for other developers") : Lang.T("beserta memory-nya", "together with its memory");
        }

        eventBus.Publish(AppEvent.Create(AppEventTypes.ProjectChanged, new { id, action = "deleted" }));
        eventBus.Publish(AppEvent.Create(AppEventTypes.MemoryChanged, new { id = (Guid?)null, action = "deleted" }));
        activityLog.Info("project", Lang.T($"Project '{project.Name}' dihapus — {outcome}.", $"Project '{project.Name}' deleted — {outcome}."), projectId: id);
        return true;
    }
}
