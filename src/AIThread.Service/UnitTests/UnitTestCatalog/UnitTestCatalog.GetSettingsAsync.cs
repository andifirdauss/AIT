using AIThread.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.UnitTests;

public sealed partial class UnitTestCatalog
{
    public async Task<UnitTestSettings> GetSettingsAsync(string projectKey, CancellationToken cancellationToken)
    {
        await using var db = await store.CreateUnitTestsAsync(cancellationToken);
        return await db.UnitTestSettings.AsNoTracking().FirstOrDefaultAsync(s => s.ProjectKey == projectKey, cancellationToken)
               ?? new UnitTestSettings { ProjectKey = projectKey };
    }
}
