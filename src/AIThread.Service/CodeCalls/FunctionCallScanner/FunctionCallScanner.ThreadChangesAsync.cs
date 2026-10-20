using System.Text;
using AIThread.Service.Workspace;
using AIThread.Service.Workspace.Structure;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.CodeCalls;

public sealed partial class FunctionCallScanner
{
    // The files a thread changed (still present) and the block of lines that differs from before the thread.
    private async Task<List<(string Path, int From, int To)>> ThreadChangesAsync(string root, Guid threadId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var snapshots = await db.ThreadFileSnapshots.AsNoTracking()
            .Where(s => s.ThreadRunId == threadId && !s.Removed)
            .Select(s => new { s.FilePath, s.Existed, s.OriginalContent })
            .ToListAsync(cancellationToken);

        var changes = new List<(string, int, int)>();
        foreach (var snapshot in snapshots)
        {
            var full = Path.Combine(root, snapshot.FilePath);
            if (OutlineLanguages.Of(full) is null || !File.Exists(full)) continue;
            var after = WorkspaceTools.SplitLines(await File.ReadAllTextAsync(full, cancellationToken));
            var before = snapshot.Existed && snapshot.OriginalContent is { } original ? WorkspaceTools.SplitLines(Encoding.UTF8.GetString(original)) : [];
            if (FunctionSpans.ChangedRange(before, after) is { } range) changes.Add((snapshot.FilePath.Replace('\\', '/'), range.From, range.To));
        }
        return changes;
    }
}
