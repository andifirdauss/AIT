using AIThread.Common.Localization;
using AIThread.Data.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryStorageService
{
    // The unit test catalog follows long memory too. A test the new store already has (same id, or same project and
    // selector) and a project whose settings it already has are left alone, so another developer's catalog there wins.
    // Number clashes get the next free number of the project.
    private async Task CopyUnitTestsAsync(MemoryStoreConnection from, MemoryStoreConnection to, List<string> warnings, CancellationToken cancellationToken)
    {
        try
        {
            await using var source = await longStore.OpenUnitTestsAsync(from, cancellationToken);
            var tests = await source.UnitTestCases.AsNoTracking().ToListAsync(cancellationToken);
            var settings = await source.UnitTestSettings.AsNoTracking().ToListAsync(cancellationToken);
            var runs = await source.UnitTestRuns.AsNoTracking().ToListAsync(cancellationToken);
            if (tests.Count + settings.Count == 0) return;

            await using var target = await longStore.OpenUnitTestsAsync(to, cancellationToken);
            var existing = await target.UnitTestCases.AsNoTracking().Select(t => new { t.Id, t.ProjectKey, t.Selector, t.Number }).ToListAsync(cancellationToken);
            var ids = existing.Select(t => t.Id).ToHashSet();
            var selectors = existing.Select(t => t.ProjectKey + "|" + t.Selector).ToHashSet(StringComparer.Ordinal);
            var numbers = existing.GroupBy(t => t.ProjectKey).ToDictionary(g => g.Key, g => g.Select(t => t.Number).ToHashSet(), StringComparer.Ordinal);
            var settingKeys = (await target.UnitTestSettings.AsNoTracking().Select(s => s.ProjectKey).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);

            var copied = new HashSet<Guid>();
            foreach (var test in tests.OrderBy(t => t.ProjectKey).ThenBy(t => t.Number))
            {
                if (ids.Contains(test.Id) || !selectors.Add(test.ProjectKey + "|" + test.Selector)) continue;
                if (!numbers.TryGetValue(test.ProjectKey, out var used)) numbers[test.ProjectKey] = used = [];
                if (used.Contains(test.Number)) test.Number = used.Max() + 1;
                used.Add(test.Number);
                target.UnitTestCases.Add(test);
                copied.Add(test.Id);
            }
            target.UnitTestSettings.AddRange(settings.Where(s => settingKeys.Add(s.ProjectKey)));
            await target.SaveChangesAsync(cancellationToken);

            foreach (var batch in runs.Where(r => copied.Contains(r.UnitTestCaseId)).Chunk(CopyBatchSize))
            {
                target.UnitTestRuns.AddRange(batch);
                await target.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The unit test catalog could not be copied to the new store.");
            warnings.Add(Lang.T($"Katalog unit test tidak dapat disalin (bisa diisi ulang lewat Scan test atau import memory): {MemoryStoreTester.ErrorMessage(ex)}", $"The unit test catalog cannot be copied (it can be filled again with Scan tests or a memory import): {MemoryStoreTester.ErrorMessage(ex)}"));
        }
    }
}
