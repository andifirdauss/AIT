using AIThread.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.UnitTests;

public sealed partial class UnitTestCatalog
{
    public async Task<IReadOnlyList<UnitTestCase>> ListAsync(string projectKey, CancellationToken cancellationToken)
    {
        await using var db = await store.CreateUnitTestsAsync(cancellationToken);
        return await db.UnitTestCases.AsNoTracking().Where(t => t.ProjectKey == projectKey).OrderBy(t => t.Number).ToListAsync(cancellationToken);
    }
}
