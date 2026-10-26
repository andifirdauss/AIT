using AIThread.Common.Enums;
using AIThread.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // The lines the thread's succeeded tasks added and removed, per file (a move counts for its target), from the task
    // diffs ("+" / "-" lines of DiffBuilder; headers and context left out).
    private static async Task<Dictionary<string, ThreadLines>> ThreadLinesAsync(AIThreadDbContext db, Guid threadId, CancellationToken cancellationToken)
    {
        var tasks = await db.ThreadTasks.AsNoTracking()
            .Where(t => t.ThreadRunId == threadId && t.Status == ThreadTaskStatus.Succeeded && t.Diff != null)
            .Select(t => new { t.FilePath, t.TargetPath, t.Diff })
            .ToListAsync(cancellationToken);

        var lines = new Dictionary<string, (HashSet<string> Added, HashSet<string> Removed)>(StringComparer.OrdinalIgnoreCase);
        foreach (var task in tasks)
        {
            var path = (task.TargetPath ?? task.FilePath).Replace('\\', '/');
            if (!lines.TryGetValue(path, out var set)) lines[path] = set = (new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));
            foreach (var line in task.Diff!.Split('\n'))
            {
                if (line.StartsWith("+++ b/", StringComparison.Ordinal) || line.StartsWith("--- a/", StringComparison.Ordinal) || line == "--- /dev/null") continue;
                if (line.StartsWith('+')) set.Added.UnionWith(Normalize(line[1..]));
                else if (line.StartsWith('-')) set.Removed.UnionWith(Normalize(line[1..]));
            }
        }
        return lines.ToDictionary(p => p.Key, p => new ThreadLines(p.Value.Added, p.Value.Removed), StringComparer.OrdinalIgnoreCase);
    }
}
