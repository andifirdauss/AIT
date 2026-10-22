using AIThread.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.UnitTests;

public sealed partial class UnitTestCatalog
{
    public async Task<IReadOnlyDictionary<Guid, UnitTestRun>> LastRunsOnMachineAsync(string projectKey, string machine, CancellationToken cancellationToken)
    {
        await using var db = await store.CreateUnitTestsAsync(cancellationToken);
        var runs = await db.UnitTestRuns.AsNoTracking()
            .Where(r => r.ProjectKey == projectKey && r.Machine == machine)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
        return runs.GroupBy(r => r.UnitTestCaseId).ToDictionary(g => g.Key, g => g.First());
    }
}
