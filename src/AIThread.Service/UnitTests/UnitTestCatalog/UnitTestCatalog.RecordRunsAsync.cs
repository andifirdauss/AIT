using AIThread.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.UnitTests;

public sealed partial class UnitTestCatalog
{
    public async Task RecordRunsAsync(IReadOnlyList<UnitTestRun> runs, CancellationToken cancellationToken)
    {
        if (runs.Count == 0) return;
        await using var db = await store.CreateUnitTestsAsync(cancellationToken);
        db.UnitTestRuns.AddRange(runs);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var testId in runs.Select(r => r.UnitTestCaseId).Distinct())
        {
            var stale = await db.UnitTestRuns.Where(r => r.UnitTestCaseId == testId)
                .OrderByDescending(r => r.CreatedAt)
                .Skip(MaxRunsPerTest)
                .Select(r => r.Id)
                .ToListAsync(cancellationToken);
            if (stale.Count > 0) await db.UnitTestRuns.Where(r => stale.Contains(r.Id)).ExecuteDeleteAsync(cancellationToken);
        }
    }
}
