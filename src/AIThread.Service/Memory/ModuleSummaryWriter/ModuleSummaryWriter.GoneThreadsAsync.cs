using AIThread.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Memory;

public sealed partial class ModuleSummaryWriter
{
    // The threads whose change was thrown away or lost in their author's clone (ThreadChanges). Empty when the store
    // cannot tell: the entries are folded as before.
    private async Task<IReadOnlySet<Guid>> GoneThreadsAsync(IEnumerable<Guid?> threadIds, CancellationToken cancellationToken)
    {
        var ids = threadIds.OfType<Guid>().Distinct().ToList();
        if (ids.Count == 0) return new HashSet<Guid>();
        try
        {
            await using var db = await longStore.CreateGitAsync(cancellationToken);
            return (await db.ThreadChanges.AsNoTracking()
                    .Where(c => ids.Contains(c.Id) && (c.State == GitChangeState.Discarded || c.State == GitChangeState.Lost))
                    .Select(c => c.Id)
                    .ToListAsync(cancellationToken))
                .ToHashSet();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The git state of the change entries could not be read");
            return new HashSet<Guid>();
        }
    }
}
