using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Git;

public sealed partial class GitEnvironmentTracker
{
    // Looks for the project key's shared changes in each environment's ref of this clone (the registered ones and the
    // remote branches) and records the new ones; the environment pages of the wiki follow. An environment whose head and
    // changes are the same as at its last pass is skipped. Returns how many changes reached an environment. Never throws.
    public async Task<int> RefreshAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        if (await ProjectAsync(projectId, cancellationToken) is not var (key, root)) return 0;
        var found = 0;
        try
        {
            await using var db = await longStore.CreateGitAsync(cancellationToken);
            var environments = await EnvironmentsAsync(db, key, root, cancellationToken);
            var scanned = new List<(string Key, string Value)>();
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
                var signature = Signature(changes);
                foreach (var environment in environments)
                {
                    if (environment.Head is not { } head) continue;
                    var scanKey = $"{key}\n{environment.Name}\n{environment.Ref}";
                    var scanValue = $"{head}\n{signature}";
                    if (_scanned.TryGetValue(scanKey, out var last) && last == scanValue) continue;
                    scanned.Add((scanKey, scanValue));
                    var missing = changes.Where(c => !seen.Contains((c.Id, environment.Name.ToLowerInvariant()))).ToList();
                    if (missing.Count == 0) continue;

                    var lookups = environment.Auto ? MaxBranchHistoryLookups : MaxHistoryLookups;
                    foreach (var (change, commit) in await FindInAsync(root, head, missing, lookups, cancellationToken))
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
                // Only once recorded: a pass that failed is done again.
                foreach (var (scanKey, scanValue) in scanned) _scanned[scanKey] = scanValue;
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
