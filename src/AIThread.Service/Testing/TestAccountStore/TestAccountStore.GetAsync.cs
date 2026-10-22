using AIThread.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Testing;

public sealed partial class TestAccountStore
{
    public async Task<TestAccountSet> GetAsync(string projectKey, CancellationToken cancellationToken = default)
    {
        await using var db = await store.CreateTestingAsync(cancellationToken);
        var accounts = await db.TestAccounts.AsNoTracking().Where(a => a.ProjectKey == projectKey).ToListAsync(cancellationToken);
        var settings = await db.TestingSettings.AsNoTracking().FirstOrDefaultAsync(s => s.ProjectKey == projectKey, cancellationToken)
                       ?? new TestingSettings { ProjectKey = projectKey };
        return new TestAccountSet(accounts.OrderBy(a => a.Username, StringComparer.OrdinalIgnoreCase).ToList(), settings);
    }
}
