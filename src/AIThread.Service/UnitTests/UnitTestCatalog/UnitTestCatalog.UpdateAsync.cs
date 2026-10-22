using AIThread.Common.Entities;

namespace AIThread.Service.UnitTests;

public sealed partial class UnitTestCatalog
{
    public async Task UpdateAsync(IReadOnlyList<UnitTestCase> tests, CancellationToken cancellationToken)
    {
        if (tests.Count == 0) return;
        var now = DateTime.UtcNow;
        foreach (var test in tests) test.UpdatedAt = now;
        await using var db = await store.CreateUnitTestsAsync(cancellationToken);
        db.UnitTestCases.UpdateRange(tests);
        await db.SaveChangesAsync(cancellationToken);
    }
}
