using AIThread.Data.Memory;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Git;

public sealed partial class GitEnvironmentTracker
{
    // The project's registered environments in their order, then this clone's remote branches that no registered one
    // stands for already (same name or same ref).
    private static async Task<IReadOnlyList<EnvironmentRef>> EnvironmentsAsync(IGitTrackingDb db, string key, string root,
        CancellationToken cancellationToken)
    {
        var registered = await db.ProjectEnvironments.AsNoTracking().Where(e => e.ProjectKey == key).OrderBy(e => e.Order).ThenBy(e => e.Name)
            .ToListAsync(cancellationToken);
        var environments = new List<EnvironmentRef>();
        foreach (var environment in registered)
            environments.Add(new EnvironmentRef(environment.Id, environment.Name, environment.Ref, environment.Order, environment.IsProduction, false,
                await ResolveRefAsync(root, environment.Ref, cancellationToken)));

        foreach (var branch in await RemoteBranchesAsync(root, cancellationToken))
        {
            if (registered.Any(e => string.Equals(e.Name, branch.Name, StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(e.Ref, branch.Ref, StringComparison.OrdinalIgnoreCase))) continue;
            environments.Add(branch with { Order = environments.Count + 1 });
        }
        return environments;
    }
}
