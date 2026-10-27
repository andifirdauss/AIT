using AIThread.Common.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryService
{
    // For the Memory menu: the git state of each memory's change in its author's clone (ThreadChanges, shared with the
    // team), without asking git here. An unreachable store only leaves the states out.
    private async Task<List<MemoryItemDto>> WithGitStatesAsync(List<MemoryItemDto> items, CancellationToken cancellationToken)
    {
        var ids = items.Select(i => i.SourceThreadId).OfType<Guid>().Distinct().ToList();
        if (ids.Count == 0) return items;
        try
        {
            await using var db = await longStore.CreateGitAsync(cancellationToken);
            var states = await db.ThreadChanges.AsNoTracking()
                .Where(c => ids.Contains(c.Id))
                .Select(c => new { c.Id, c.State, c.Ingested })
                .ToDictionaryAsync(c => c.Id, cancellationToken);
            return items.Select(i => i.SourceThreadId is { } id && states.TryGetValue(id, out var row)
                ? i with { GitState = row.State, FromCommit = row.Ingested }
                : i).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LongStoreFailed(ex, "Could not read the git state of the memories");
            return items;
        }
    }
}
