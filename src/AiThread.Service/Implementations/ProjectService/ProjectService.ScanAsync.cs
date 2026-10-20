using System.Diagnostics;
using AiThread.Common.DTOs;
using AiThread.Service.Models;
using AiThread.Service.Workspace;

namespace AiThread.Service.Implementations;

public sealed partial class ProjectService
{
    public async Task<ProjectScanDto> ScanAsync(string path, int depth, CancellationToken cancellationToken = default)
    {
        var directory = ExistingDirectory(path) ?? throw new AppValidationException($"Folder '{path}' tidak ditemukan.");
        depth = Math.Clamp(depth, 1, 8);

        using var timeLimit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeLimit.CancelAfter(ScanTimeLimit);
        var stopwatch = Stopwatch.StartNew();
        var result = await Task.Run(() => ProjectLocator.Scan(directory, depth, MaxScanResults, MaxScanFolders, timeLimit.Token), cancellationToken);
        stopwatch.Stop();

        var registered = await RegisteredRootsAsync(cancellationToken);
        return new ProjectScanDto(
            directory,
            depth,
            result.Projects.Select(p => ToCandidateDto(p, registered)).ToList(),
            result.FoldersScanned,
            result.Truncated,
            stopwatch.ElapsedMilliseconds);
    }
}
