using AIThread.Data.Memory;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Git;

public sealed partial class CommitIngestor
{
    // What AIT already knows of the project key: the commits taken on before (by any AIT), the commits of AIT threads and
    // the environment commits that brought them (squash, merge), and every path + blob an AIT thread wrote.
    private static async Task<(HashSet<string> Commits, HashSet<string> ThreadFiles)> KnownAsync(IGitTrackingDb db, string key, CancellationToken cancellationToken)
    {
        var commits = (await db.IngestedCommits.AsNoTracking().Where(c => c.ProjectKey == key).Select(c => c.Commit).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        commits.UnionWith((await db.ChangePresences.AsNoTracking().Where(p => p.ProjectKey == key && p.Commit != null).Select(p => p.Commit!)
            .ToListAsync(cancellationToken)));

        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var threads = await db.ThreadChanges.AsNoTracking().Where(c => c.ProjectKey == key && !c.Ingested)
            .Select(c => new { c.Commits, c.Files }).ToListAsync(cancellationToken);
        foreach (var thread in threads)
        {
            commits.UnionWith(ThreadChangeFiles.Commits(thread.Commits));
            foreach (var file in ThreadChangeFiles.Read(thread.Files).Where(f => f.Blob is not null)) files.Add($"{file.Path}|{file.Blob}");
        }
        return (commits, files);
    }
}
