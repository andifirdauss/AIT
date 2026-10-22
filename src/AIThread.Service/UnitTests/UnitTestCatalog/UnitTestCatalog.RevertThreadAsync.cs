using AIThread.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.UnitTests;

public sealed partial class UnitTestCatalog
{
    // Never fails the rollback: a catalog that cannot be reached keeps the rows, and the next scan sets them right.
    public async Task<int> RevertThreadAsync(Guid threadId, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await store.CreateUnitTestsAsync(cancellationToken);
            var created = await db.UnitTestCases.Where(t => t.CreatedThreadId == threadId && t.Origin == UnitTestOrigin.Ait)
                .Select(t => t.Id).ToListAsync(cancellationToken);
            if (created.Count > 0)
            {
                await db.UnitTestRuns.Where(r => created.Contains(r.UnitTestCaseId)).ExecuteDeleteAsync(cancellationToken);
                await db.UnitTestCases.Where(t => created.Contains(t.Id)).ExecuteDeleteAsync(cancellationToken);
            }

            var restored = await db.UnitTestCases.Where(t => t.ObsoleteThreadId == threadId && t.State == UnitTestState.Obsolete)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.State, UnitTestState.Active)
                    .SetProperty(t => t.ObsoleteReason, (string?)null)
                    .SetProperty(t => t.ObsoleteThreadId, (Guid?)null)
                    .SetProperty(t => t.ObsoleteBy, (string?)null)
                    .SetProperty(t => t.ObsoleteAt, (DateTime?)null), cancellationToken);
            return created.Count + restored;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The unit test catalog could not be reverted for thread {ThreadId}.", threadId);
            return 0;
        }
    }
}
