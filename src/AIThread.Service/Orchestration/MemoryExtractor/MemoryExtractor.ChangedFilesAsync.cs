using AIThread.Common.Enums;
using AIThread.Service.Memory;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Orchestration;

public sealed partial class MemoryExtractor
{
    // The files the thread really changed, from its file snapshots (a file it created and then removed is left out).
    // Without snapshots (an older thread, or the journal failed) the files of the succeeded tasks stand in.
    private async Task<IReadOnlyList<ChangeLogFile>> ChangedFilesAsync(Guid threadId, ExecutionSummary? execution, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var snapshots = await db.ThreadFileSnapshots.AsNoTracking()
            .Where(s => s.ThreadRunId == threadId)
            .Select(s => new { s.FilePath, s.Existed, s.Removed })
            .ToListAsync(cancellationToken);
        if (snapshots.Count > 0)
        {
            return snapshots
                .Where(s => s.Existed || !s.Removed)
                .Select(s => new ChangeLogFile(ChangeLog.NormalizePath(s.FilePath), !s.Existed ? ChangeLog.Created : s.Removed ? ChangeLog.Deleted : ChangeLog.Modified, null))
                .DistinctBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return (execution?.Results ?? [])
            .Where(r => r.Status == ThreadTaskStatus.Succeeded && !string.IsNullOrWhiteSpace(r.File))
            .Select(r => new ChangeLogFile(ChangeLog.NormalizePath(r.File), ChangeLog.Modified, null))
            .DistinctBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
