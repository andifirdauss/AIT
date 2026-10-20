using AiThread.Service.Models;
using AiThread.Service.Workspace;
using AiThread.Service.Workspace.Structure;

namespace AiThread.Service.Implementations;

public sealed partial class CodeStructureService
{
    private async Task<StructureSnapshot> SnapshotAsync(Guid projectId, bool rescan, CancellationToken cancellationToken)
    {
        var entry = _projects.GetOrAdd(projectId, _ => new Entry());
        entry.LastUsed = DateTime.UtcNow;
        if (!rescan && entry.Snapshot is { } current && DateTime.UtcNow - current.ScannedAt < FreshFor) return current;

        await entry.Gate.WaitAsync(cancellationToken);
        try
        {
            // Another request may have scanned while this one waited.
            if (!rescan && entry.Snapshot is { } scanned && DateTime.UtcNow - scanned.ScannedAt < FreshFor) return scanned;

            var project = await projectService.GetContextAsync(projectId, cancellationToken) ?? throw new AppValidationException("Project tidak ditemukan.");
            if (!Directory.Exists(project.RootPath)) throw new AppValidationException($"Folder project '{project.RootPath}' tidak ditemukan di mesin ini.");

            var ignored = ProjectLocator.IgnoredDirectoriesFor(project.Stacks);
            var previous = entry.Snapshot;
            var snapshot = await Task.Run(() => CodeStructureScanner.Scan(project.RootPath, ignored, previous, cancellationToken), cancellationToken);
            entry.Snapshot = snapshot;
            Evict(projectId);
            return snapshot;
        }
        finally
        {
            entry.Gate.Release();
        }
    }
}
