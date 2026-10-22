using AIThread.Common.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.UnitTests;

public sealed partial class UnitTestCatalog
{
    public async Task<IReadOnlyList<UnitTestCase>> AddAsync(string projectKey, IReadOnlyList<UnitTestCase> tests, CancellationToken cancellationToken)
    {
        if (tests.Count == 0) return [];
        for (var attempt = 1; ; attempt++)
        {
            await using var db = await store.CreateUnitTestsAsync(cancellationToken);
            var next = (await db.UnitTestCases.Where(t => t.ProjectKey == projectKey).MaxAsync(t => (int?)t.Number, cancellationToken) ?? 0) + 1;
            foreach (var test in tests)
            {
                test.ProjectKey = projectKey;
                test.Number = next++;
            }
            db.UnitTestCases.AddRange(tests);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return tests;
            }
            catch (DbUpdateException ex) when (attempt < MaxNumberAttempts)
            {
                logger.LogInformation(ex, "Unit test numbers of {ProjectKey} were taken meanwhile; trying the next ones.", projectKey);
            }
        }
    }
}
