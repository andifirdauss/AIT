using AIThread.Common.Entities;
using AIThread.Service.Models;
using AIThread.Service.Workspace;

namespace AIThread.Service.Memory;

public sealed partial class CodeMapper
{
    // Maps every source file of the project that has no entry yet (first registration, relabeling of old memory). Every
    // file gets an entry (by the model or by its name stem), so a scan cut off halfway picks up where it stopped.
    // progress: (files mapped so far, files in the scan), after the stored/rule pass and after every model batch.
    public async Task<(int Files, int Mapped)> ScanAsync(ProjectContext project, int maxFiles, CancellationToken cancellationToken,
        Action<int, int>? progress = null)
    {
        if (string.IsNullOrEmpty(project.Key) || !Directory.Exists(project.RootPath)) return (0, 0);
        var scope = new WorkspaceScope(project.RootPath, ProjectLocator.IgnoredDirectoriesFor(project.Stacks));
        var files = scope.EnumerateFiles(maxFiles: maxFiles * 4)
            .Select(scope.ToRelative)
            .Where(p => p.Length <= CodeMapEntry.MaxPathLength && !scope.IsIgnoredPath(p) && CodeMapHeuristics.IsMappable(p))
            .Take(maxFiles)
            .ToList();
        return await MapFilesAsync(project, files, progress, cancellationToken);
    }
}
