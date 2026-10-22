using AIThread.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.UnitTests;

public sealed partial class UnitTestCatalog
{
    public async Task<UnitTestCase?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await store.CreateUnitTestsAsync(cancellationToken);
        return await db.UnitTestCases.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }
}
