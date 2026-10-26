using AIThread.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Business;

public sealed partial class BusinessDigestCollector
{
    // Project keys of this machine that have threads waiting.
    public async Task<IReadOnlyList<(string Key, string Name)>> PendingProjectsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Threads.AsNoTracking()
            .Where(t => t.Status == ThreadStatus.Completed && t.BusinessDigestedAt == null && t.Project != null && t.Project.ProjectKey != "")
            .Select(t => new { t.Project!.ProjectKey, t.Project.Name })
            .Distinct()
            .ToListAsync(cancellationToken);
        var projects = rows.GroupBy(r => r.ProjectKey).Select(g => (g.Key, g.First().Name)).ToList();
        try
        {
            // Keys with only commits waiting, named after their project on this machine.
            await using var git = await longStore.CreateGitAsync(cancellationToken);
            var keys = await PendingCommits(git, null).Select(c => c.ProjectKey).Distinct().ToListAsync(cancellationToken);
            foreach (var key in keys.Where(k => projects.All(p => p.Key != k)))
            {
                var name = await db.Projects.AsNoTracking().Where(p => p.ProjectKey == key).Select(p => p.Name).FirstOrDefaultAsync(cancellationToken);
                if (name is not null) projects.Add((key, name));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The project keys with summarized commits could not be read");
        }
        return projects;
    }
}
