using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Git;

public sealed partial class GitEnvironmentTracker
{
    // Looks for the project key's shared changes in each environment's ref of this clone and records the new ones; the
    // environment pages of the wiki follow. Returns how many changes reached an environment. Never throws.
    public async Task<int> RefreshAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        if (await ProjectAsync(projectId, cancellationToken) is not var (key, root)) return 0;
        var found = 0;
        try
        {
            await using var db = await longStore.CreateGitAsync(cancellationToken);
            var environments = await db.ProjectEnvironments.AsNoTracking().Where(e => e.ProjectKey == key).ToListAsync(cancellationToken);
            if (environments.Count > 0 && Directory.Exists(root) && GitTools.IsRepository(root))
            {
                var since = DateTime.UtcNow - LookBack;
                var changes = (await db.ThreadChanges.AsNoTracking()
                        .Where(c => c.ProjectKey == key && c.CreatedAt >= since)
                        .OrderByDescending(c => c.CreatedAt)
                        .Take(MaxChanges)
                        .Select(c => new { c.Id, c.CreatedAt, c.Commits, c.Files })
                        .ToListAsync(cancellationToken))
                    .Select(c => new Candidate(c.Id, c.CreatedAt, ThreadChangeFiles.Commits(c.Commits), ThreadChangeFiles.Read(c.Files)))
                    .Where(c => c.Commits.Count > 0 || c.Files.Any(f => f.Removed || f.Blob is not null))
                    .ToList();
                var ids = changes.Select(c => c.Id).ToList();
                var seen = (await db.ChangePresences.AsNoTracking().Where(p => ids.Contains(p.ThreadId))
                        .Select(p => new { p.ThreadId, p.Environment }).ToListAsync(cancellationToken))
                    .Select(p => (p.ThreadId, p.Environment.ToLowerInvariant()))
                    .ToHashSet();

                var by = GitIdentity.UserName;
                foreach (var environment in environments)
                {
                    var head = await ResolveRefAsync(root, environment.Ref, cancellationToken);
                    if (head is null) continue;
                    var missing = changes.Where(c => !seen.Contains((c.Id, environment.Name.ToLowerInvariant()))).ToList();
                    if (missing.Count == 0) continue;

                    foreach (var (change, commit) in await FindInAsync(root, head, missing, cancellationToken))
                    {
                        db.ChangePresences.Add(new ChangePresence
                        {
                            Id = Guid.NewGuid(), ProjectKey = key, ThreadId = change, Environment = environment.Name, Commit = commit, SeenBy = by
                        });
                        found++;
                    }
                }
                // Another AIT may record the same change at the same moment: the unique index keeps one, this pass retries
                // next time without the duplicates.
                if (found > 0) await db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Looking for the changes of project {ProjectId} in its environments failed", projectId);
            found = 0;
        }

        if (found > 0) eventBus.Publish(AppEvent.Create(AppEventTypes.ProjectChanged, new { Id = projectId, Action = "environments" }));
        await envPages.RefreshAsync(key, cancellationToken);
        return found;
    }
}
