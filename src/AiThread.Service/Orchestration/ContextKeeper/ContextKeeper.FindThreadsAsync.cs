using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Orchestration;

public sealed partial class ContextKeeper
{
    // Short id -> thread of this project. Short ids are only unique in practice, so the search is limited to one
    // project and a recent window.
    private async Task<Dictionary<string, (Guid Id, bool HasPack)>> FindThreadsAsync(Guid projectId, IEnumerable<string> shortIds, CancellationToken cancellationToken)
    {
        var wanted = shortIds.ToHashSet(StringComparer.Ordinal);
        var cutoff = DateTime.UtcNow - LookBack;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Threads.AsNoTracking()
            .Where(t => t.ProjectId == projectId && t.CreatedAt >= cutoff)
            .Select(t => new { t.Id, HasPack = t.ContextPack != null })
            .ToListAsync(cancellationToken);

        return rows
            .Where(r => wanted.Contains(ThreadAnchor.ShortId(r.Id)))
            .GroupBy(r => ThreadAnchor.ShortId(r.Id))
            .ToDictionary(g => g.Key, g => (g.First().Id, g.First().HasPack), StringComparer.Ordinal);
    }
}
