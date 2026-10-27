using AIThread.Common.Entities;
using AIThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // Writes the ThreadChanges rows other developers read: for the threads whose state changed, and for those the store
    // does not hold yet (a thread from before sharing, or a store switched since). A store that cannot be reached only
    // delays it to the next refresh.
    private async Task ShareAsync(Guid projectId, IReadOnlyList<Guid> threadIds, IReadOnlyList<Guid> changed, CancellationToken cancellationToken)
    {
        try
        {
            await using var main = await dbFactory.CreateDbContextAsync(cancellationToken);
            var project = await main.Projects.AsNoTracking().Where(p => p.Id == projectId)
                .Select(p => new { p.ProjectKey, p.Name }).FirstOrDefaultAsync(cancellationToken);
            if (project is null || string.IsNullOrEmpty(project.ProjectKey)) return;

            await using var shared = await longStore.CreateGitAsync(cancellationToken);
            var present = await shared.ThreadChanges.Where(c => threadIds.Contains(c.Id)).Select(c => c.Id).ToListAsync(cancellationToken);
            var due = threadIds.Where(id => changed.Contains(id) || !present.Contains(id)).ToList();
            if (due.Count == 0) return;

            var threads = await main.Threads.AsNoTracking()
                .Where(t => due.Contains(t.Id) && t.GitCheckedAt != null)
                .Select(t => new { t.Id, t.Prompt, t.GitAuthor, t.GitBranch, t.GitBaseCommit, t.GitState, t.GitCommits })
                .ToListAsync(cancellationToken);
            var files = (await main.ThreadFileSnapshots.AsNoTracking()
                    .Where(s => due.Contains(s.ThreadRunId))
                    .Select(s => new { s.ThreadRunId, s.FilePath, s.AfterBlob, s.Removed })
                    .ToListAsync(cancellationToken))
                .ToLookup(s => s.ThreadRunId);
            var rows = await shared.ThreadChanges.Where(c => due.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
            var by = GitIdentity.UserName;

            foreach (var thread in threads)
            {
                if (!rows.TryGetValue(thread.Id, out var row))
                {
                    row = new ThreadChange { Id = thread.Id };
                    shared.ThreadChanges.Add(row);
                }
                row.ProjectKey = project.ProjectKey;
                row.ProjectName = project.Name;
                var title = string.Join(' ', thread.Prompt.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                row.Title = Clip(title, ThreadChange.MaxTitleLength);
                row.GitAuthor = thread.GitAuthor;
                row.GitBranch = thread.GitBranch;
                row.GitBaseCommit = thread.GitBaseCommit;
                row.State = thread.GitState;
                row.Commits = thread.GitCommits;
                row.Files = ThreadChangeFiles.Write(files[thread.Id].Select(f => new SharedFile(f.FilePath, f.AfterBlob, f.Removed)));
                row.UpdatedBy = Clip(by, 300);
                row.UpdatedAt = DateTime.UtcNow;
            }
            await shared.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Sharing the git state of the threads of project {ProjectId} failed", projectId);
        }
    }
}
