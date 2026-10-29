using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Business;

public sealed partial class BusinessEnvService
{
    // Brings the project key's environment pages in line with the working wiki and the changes seen in each environment.
    // Returns how many pages were written or removed. Never throws.
    public async Task<int> RefreshAsync(string projectKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(projectKey)) return 0;
        try
        {
            List<string> environments;
            Dictionary<Guid, GitChangeState?> changes;
            HashSet<(Guid, string)> presence;
            await using (var git = await longStore.CreateGitAsync(cancellationToken))
            {
                environments = await git.ProjectEnvironments.AsNoTracking().Where(e => e.ProjectKey == projectKey).Select(e => e.Name).ToListAsync(cancellationToken);
                changes = await git.ThreadChanges.AsNoTracking().Where(c => c.ProjectKey == projectKey)
                    .Select(c => new { c.Id, c.State }).ToDictionaryAsync(c => c.Id, c => c.State, cancellationToken);
                presence = (await git.ChangePresences.AsNoTracking().Where(p => p.ProjectKey == projectKey)
                        .Select(p => new { p.ThreadId, p.Environment }).ToListAsync(cancellationToken))
                    .Select(p => (p.ThreadId, p.Environment.ToLowerInvariant()))
                    .ToHashSet();
            }

            await using var db = await store.CreateAsync(cancellationToken);
            var pages = await db.BusinessPages.AsNoTracking().Where(p => p.ProjectKey == projectKey).ToListAsync(cancellationToken);
            var pageIds = pages.Select(p => p.Id).ToList();
            var revisions = (await db.BusinessPageRevisions.AsNoTracking().Where(r => pageIds.Contains(r.PageId)).ToListAsync(cancellationToken))
                .ToLookup(r => r.PageId);
            var existing = await db.BusinessEnvPages.Where(p => p.ProjectKey == projectKey).ToListAsync(cancellationToken);

            var written = 0;
            foreach (var stale in existing.Where(e => !environments.Contains(e.Environment, StringComparer.OrdinalIgnoreCase) || !pageIds.Contains(e.PageId)))
            {
                db.BusinessEnvPages.Remove(stale);
                written++;
            }

            foreach (var environment in environments)
            {
                bool Holds(Guid thread) =>
                    !changes.TryGetValue(thread, out var state) || state is GitChangeState.Discarded or GitChangeState.Lost
                    || presence.Contains((thread, environment.ToLowerInvariant()));

                foreach (var page in pages)
                {
                    var (best, threads) = BestRevision(revisions[page.Id], Holds);

                    var row = existing.FirstOrDefault(e => e.PageId == page.Id && string.Equals(e.Environment, environment, StringComparison.OrdinalIgnoreCase));
                    if (best is null)
                    {
                        if (row is null) continue;
                        db.BusinessEnvPages.Remove(row);
                        written++;
                        continue;
                    }
                    if (row is not null && row.Version == best.Version && row.Module == page.Module && row.Slug == page.Slug) continue;
                    if (row is null)
                    {
                        row = new BusinessEnvPage { Id = Guid.NewGuid(), ProjectKey = projectKey, Environment = environment, PageId = page.Id };
                        db.BusinessEnvPages.Add(row);
                    }
                    row.Module = page.Module;
                    row.Title = best.Title;
                    row.Slug = page.Slug;
                    row.Summary = best.Version == page.Version ? page.Summary : row.Summary;
                    row.Content = best.Content;
                    row.Version = best.Version;
                    row.SourceThreadIds = threads.Count == 0 ? null : string.Join(',', threads.Distinct());
                    row.UpdatedAt = DateTime.UtcNow;
                    written++;
                }
            }

            if (written == 0) return 0;
            await db.SaveChangesAsync(cancellationToken);
            eventBus.Publish(AppEvent.Create(AppEventTypes.BusinessChanged, new { Action = "environments", ProjectKey = projectKey }));
            return written;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The environment pages of {ProjectKey} could not be updated", projectKey);
            return 0;
        }
    }
}
