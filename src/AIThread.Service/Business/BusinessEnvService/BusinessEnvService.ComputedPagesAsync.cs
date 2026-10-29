using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Data.Memory;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Business;

public sealed partial class BusinessEnvService
{
    // The pages of an environment that is not registered (a remote branch), worked out on request instead of being
    // copied for every branch: what RefreshAsync would keep for it. pageId: only that page. The rows are not saved.
    private async Task<List<BusinessEnvPage>> ComputedPagesAsync(IBusinessMemoryDb db, string projectKey, string environment, Guid? pageId,
        CancellationToken cancellationToken)
    {
        Dictionary<Guid, GitChangeState?> changes;
        HashSet<Guid> seen;
        await using (var git = await longStore.CreateGitAsync(cancellationToken))
        {
            changes = await git.ThreadChanges.AsNoTracking().Where(c => c.ProjectKey == projectKey)
                .Select(c => new { c.Id, c.State }).ToDictionaryAsync(c => c.Id, c => c.State, cancellationToken);
            seen = (await git.ChangePresences.AsNoTracking().Where(p => p.ProjectKey == projectKey && p.Environment == environment)
                    .Select(p => p.ThreadId).ToListAsync(cancellationToken))
                .ToHashSet();
        }
        bool Holds(Guid thread) =>
            !changes.TryGetValue(thread, out var state) || state is GitChangeState.Discarded or GitChangeState.Lost || seen.Contains(thread);

        var pages = await db.BusinessPages.AsNoTracking().Where(p => p.ProjectKey == projectKey && (pageId == null || p.Id == pageId))
            .ToListAsync(cancellationToken);
        var ids = pages.Select(p => p.Id).ToList();
        var revisions = (await db.BusinessPageRevisions.AsNoTracking().Where(r => ids.Contains(r.PageId)).ToListAsync(cancellationToken))
            .ToLookup(r => r.PageId);

        var result = new List<BusinessEnvPage>();
        foreach (var page in pages)
        {
            var (best, threads) = BestRevision(revisions[page.Id], Holds);
            if (best is null) continue;
            result.Add(new BusinessEnvPage
            {
                Id = Guid.Empty,
                ProjectKey = projectKey,
                Environment = environment,
                PageId = page.Id,
                Module = page.Module,
                Title = best.Title,
                Slug = page.Slug,
                Summary = best.Version == page.Version ? page.Summary : null,
                Content = best.Content,
                Version = best.Version,
                SourceThreadIds = threads.Count == 0 ? null : string.Join(',', threads.Distinct()),
                CreatedAt = best.CreatedAt,
                UpdatedAt = best.CreatedAt
            });
        }
        return result;
    }
}
