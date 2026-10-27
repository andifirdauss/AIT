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
        var environments = await db.ProjectEnvironments.AsNoTracking().Where(e => e.ProjectKey == key).OrderBy(e => e.Order).ThenBy(e => e.Name)
            .ToListAsync(cancellationToken);
        var seen = (await db.ChangePresences.AsNoTracking().Where(p => p.ProjectKey == key)
                .GroupBy(p => p.Environment)
                .Select(g => new { Environment = g.Key, Count = g.Count(), Last = g.Max(p => p.CreatedAt) })
                .ToListAsync(cancellationToken))
            .ToDictionary(g => g.Environment, StringComparer.OrdinalIgnoreCase);

        var result = new List<ProjectEnvironmentDto>();
        foreach (var environment in environments)
        {
            var commit = await ResolveRefAsync(root, environment.Ref, cancellationToken);
            var stats = seen.GetValueOrDefault(environment.Name);
            result.Add(new ProjectEnvironmentDto(environment.Id, environment.Name, environment.Ref, environment.Order, environment.IsProduction,
                commit is not null, commit, stats?.Count ?? 0, stats?.Last));
        }
        return result;
    }
}
