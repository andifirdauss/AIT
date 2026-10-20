using AIThread.Service.Models;
using AIThread.Service.Proxy;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Orchestration;

public sealed partial class ContextKeeper
{
    public async Task<ResolvedSession?> ResolveAsync(Guid projectId, IReadOnlyList<ThreadAnchorRef> anchors, CancellationToken cancellationToken)
    {
        if (anchors.Count == 0) return null;
        var known = await FindThreadsAsync(projectId, anchors.Select(a => a.ShortId), cancellationToken);

        var chain = new List<(Guid Id, bool HasPack, int MessageIndex)>();
        foreach (var anchor in anchors)
        {
            if (known.TryGetValue(anchor.ShortId, out var thread) && chain.All(c => c.Id != thread.Id))
                chain.Add((thread.Id, thread.HasPack, anchor.MessageIndex));
        }
        if (chain.Count == 0) return null;

        var ids = chain.Select(c => c.Id).ToList();
        var packed = chain.FindLastIndex(c => c.HasPack);
        if (packed < 0) return new ResolvedSession(new SessionContext(null, null, ids), null);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var packThreadId = chain[packed].Id;
        var pack = await db.Threads.AsNoTracking()
            .Where(t => t.Id == packThreadId)
            .Select(t => t.ContextPack)
            .FirstOrDefaultAsync(cancellationToken);

        return new ResolvedSession(new SessionContext(packThreadId, pack, ids), chain[packed].MessageIndex);
    }
}
