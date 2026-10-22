using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectMemoryKeys
{
    // The unit test catalog moves with the memory. A test the new key already has (same selector) is dropped; a number
    // the new key already uses gets the next free one; settings the new key already has stay.
    private async Task RekeyUnitTestsAsync(string oldKey, string newKey, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await longStore.CreateUnitTestsAsync(cancellationToken);
            var taken = await db.UnitTestCases.AsNoTracking().Where(t => t.ProjectKey == newKey).Select(t => new { t.Selector, t.Number }).ToListAsync(cancellationToken);
            var selectors = taken.Select(t => t.Selector).ToHashSet(StringComparer.Ordinal);
            var numbers = taken.Select(t => t.Number).ToHashSet();
            var tests = await db.UnitTestCases.Where(t => t.ProjectKey == oldKey).OrderBy(t => t.Number).ToListAsync(cancellationToken);
            foreach (var test in tests)
            {
                if (!selectors.Add(test.Selector))
                {
                    db.UnitTestCases.Remove(test);
                    continue;
                }
                if (numbers.Contains(test.Number)) test.Number = numbers.Max() + 1;
                numbers.Add(test.Number);
                test.ProjectKey = newKey;
            }

            var settings = await db.UnitTestSettings.FirstOrDefaultAsync(s => s.ProjectKey == oldKey, cancellationToken);
            if (settings is not null)
            {
                if (await db.UnitTestSettings.AnyAsync(s => s.ProjectKey == newKey, cancellationToken)) db.UnitTestSettings.Remove(settings);
                else settings.ProjectKey = newKey;
            }
            await db.SaveChangesAsync(cancellationToken);

            await db.UnitTestRuns.Where(r => r.ProjectKey == oldKey)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.ProjectKey, newKey), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not move the unit test catalog from {OldKey} to {NewKey}.", oldKey, newKey);
        }
    }
}
