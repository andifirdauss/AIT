using AIThread.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.UnitTests;

public sealed partial class UnitTestCatalog
{
    public async Task<IReadOnlyList<UnitTestRun>> HistoryAsync(Guid testId, int take, CancellationToken cancellationToken)
    {
        await using var db = await store.CreateUnitTestsAsync(cancellationToken);
        return await db.UnitTestRuns.AsNoTracking().Where(r => r.UnitTestCaseId == testId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(Math.Clamp(take, 1, MaxRunsPerTest))
            .ToListAsync(cancellationToken);
    }
}
