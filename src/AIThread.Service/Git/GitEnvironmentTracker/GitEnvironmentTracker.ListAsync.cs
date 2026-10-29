using AIThread.Common.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Git;

public sealed partial class GitEnvironmentTracker
{
    // Null when the project is not registered (or has no key yet).
    public async Task<IReadOnlyList<ProjectEnvironmentDto>?> ListAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        if (await ProjectAsync(projectId, cancellationToken) is not var (key, root)) return null;
        await using var db = await longStore.CreateGitAsync(cancellationToken);
        var environments = await EnvironmentsAsync(db, key, root, cancellationToken);
        var seen = (await db.ChangePresences.AsNoTracking().Where(p => p.ProjectKey == key)
                .GroupBy(p => p.Environment)
                .Select(g => new { Environment = g.Key, Count = g.Count(), Last = g.Max(p => p.CreatedAt) })
                .ToListAsync(cancellationToken))
            .ToDictionary(g => g.Environment, StringComparer.OrdinalIgnoreCase);

        return environments.Select(e =>
            {
                var stats = seen.GetValueOrDefault(e.Name);
                return new ProjectEnvironmentDto(e.Id, e.Name, e.Ref, e.Order, e.IsProduction, e.Head is not null, e.Head, stats?.Count ?? 0, stats?.Last,
                    e.Auto);
            })
            .ToList();
    }
}
