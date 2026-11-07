using AIThread.Common.Enums;
using AIThread.Service.Security;
using AIThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // What the working tree of the project holds that is not committed and not an AIT thread's: files changed by hand,
    // in an IDE or by another CLI talking to its provider directly (AIT never saw that prompt). A file an AIT thread of
    // the last TrackedFor wrote counts as AIT's while it still holds exactly what the thread left (its AfterHash);
    // changed since, it is listed with AfterAit. Only files under the project root, git-ignored ones and credential files
    // left out, no model and no file content: git diff, git ls-files and the hash of the files AIT wrote. Empty outside
    // a repository, before the first commit, or when git fails; never throws.
    public async Task<OutsideChangeSet> OutsideChangesAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        try
        {
            string? root;
            Dictionary<string, (HashSet<string?> Hashes, bool Removed)> written = new(StringComparer.OrdinalIgnoreCase);
            await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
            {
                root = await db.Projects.AsNoTracking().Where(p => p.Id == projectId).Select(p => p.RootPath).FirstOrDefaultAsync(cancellationToken);
                if (root is null || !Directory.Exists(root) || !GitTools.IsRepository(root)) return OutsideChangeSet.None;
                var after = DateTime.UtcNow - TrackedFor;
                var snapshots = await db.ThreadFileSnapshots.AsNoTracking()
                    .Where(s => s.ThreadRun!.ProjectId == projectId && s.ThreadRun.Status != ThreadStatus.RolledBack && s.CreatedAt > after)
                    .Select(s => new { s.FilePath, s.AfterHash, s.Removed })
                    .ToListAsync(cancellationToken);
                foreach (var snapshot in snapshots)
                {
                    var entry = written.TryGetValue(snapshot.FilePath, out var known) ? known : ([], false);
                    entry.Hashes.Add(snapshot.AfterHash);
                    written[snapshot.FilePath] = (entry.Hashes, entry.Removed || snapshot.Removed);
                }
            }

            // "M\0path\0", relative to root and only below it (--relative); fails before the first commit.
            var timeout = TimeSpan.FromSeconds(10);
            var status = await GitProcess.RunAsync(root, ["diff", "HEAD", "--name-status", "-z", "--no-renames", "--relative"], cancellationToken, timeout: timeout);
            if (!status.Ok) return OutsideChangeSet.None;
            var changes = new List<(string Path, string Status)>();
            var entries = GitProcess.Entries(status.Output);
            for (var i = 0; i + 1 < entries.Count; i += 2)
                changes.Add((entries[i + 1], entries[i].StartsWith('D') ? "deleted" : entries[i].StartsWith('A') ? "added" : "modified"));
            var untracked = await GitProcess.RunAsync(root, ["ls-files", "--others", "--exclude-standard", "-z"], cancellationToken, timeout: timeout);
            changes.AddRange(GitProcess.Entries(untracked.Output).Select(p => (p, "new")));

            // "<added>\t<removed>\t<path>\0" ("-" for a binary file).
            var numstat = await GitProcess.RunAsync(root, ["diff", "HEAD", "--numstat", "-z", "--no-renames", "--relative"], cancellationToken, timeout: timeout);
            var lines = new Dictionary<string, (int? Added, int? Removed)>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in GitProcess.Entries(numstat.Output))
                if (entry.Split('\t', 3) is [var added, var removed, var path])
                    lines[path] = (int.TryParse(added, out var a) ? a : null, int.TryParse(removed, out var r) ? r : null);

            var files = new List<OutsideFile>();
            foreach (var (path, change) in changes.DistinctBy(c => c.Path, StringComparer.OrdinalIgnoreCase))
            {
                var full = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
                if (path.StartsWith(".aithread/", StringComparison.OrdinalIgnoreCase) || SecretPaths.IsProtected(full, root, null)) continue;
                var afterAit = false;
                if (written.TryGetValue(path, out var ait))
                {
                    var hash = CurrentHash(root, path);
                    if (hash is null ? ait.Removed : ait.Hashes.Contains(hash)) continue;
                    afterAit = true;
                }
                (int? Added, int? Removed) counted = lines.TryGetValue(path, out var numbers) ? numbers : (change == "new" ? CountLines(full) : null, null);
                files.Add(new OutsideFile(path, change, counted.Added, counted.Removed, afterAit));
            }
            return new OutsideChangeSet(files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).Take(OutsideChangeSet.MaxFiles).ToList(), files.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Reading the uncommitted changes made outside AIT failed for project {ProjectId}", projectId);
            return OutsideChangeSet.None;
        }

        // Lines of a new text file, when small enough to read.
        static int? CountLines(string full)
        {
            try
            {
                var info = new FileInfo(full);
                if (!info.Exists || info.Length > 512 * 1024) return null;
                var bytes = File.ReadAllBytes(full);
                if (Array.IndexOf(bytes, (byte)0) >= 0) return null;
                var count = bytes.Count(b => b == (byte)'\n');
                return bytes.Length > 0 && bytes[^1] != (byte)'\n' ? count + 1 : count;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
