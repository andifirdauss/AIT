using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Git;

public sealed partial class GitEnvironmentTracker
{
    // The threads whose change was seen in the environment.
    public async Task<IReadOnlySet<Guid>> ThreadsInAsync(string projectKey, string environment, CancellationToken cancellationToken = default)
    {
        await using var db = await longStore.CreateGitAsync(cancellationToken);
        return (await db.ChangePresences.AsNoTracking()
                .Where(p => p.ProjectKey == projectKey && p.Environment == environment)
                .Select(p => p.ThreadId)
                .ToListAsync(cancellationToken))
            .ToHashSet();
    }
}
