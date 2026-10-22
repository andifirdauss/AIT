using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryTransferService
{
    // The unit tests of a file: the same test (by id, else by project and selector) is updated only when the file's copy
    // is newer; a new one keeps its number unless the project already uses it, then it gets the next free one.
    private async Task<UnitTestImport> ImportUnitTestsAsync(IReadOnlyList<UnitTestExportItem> items, bool dryRun, CancellationToken cancellationToken)
    {
        if (items.Count == 0) return UnitTestImport.None;

        var warnings = new List<string>();
        var valid = new List<UnitTestExportItem>();
        for (var i = 0; i < items.Count; i++)
        {
            if (InvalidUnitTest(items[i]) is { } reason) warnings.Add($"Unit test #{i + 1}: {reason}");
            else valid.Add(items[i]);
        }

        var keys = valid.Select(v => v.ProjectKey.Trim()).Distinct(StringComparer.Ordinal).ToList();
        await using var db = await longStore.CreateUnitTestsAsync(cancellationToken);
        var existing = await db.UnitTestCases.Where(t => keys.Contains(t.ProjectKey)).ToListAsync(cancellationToken);
        var noSnapshots = (await db.UnitTestSettings.Where(s => keys.Contains(s.ProjectKey) && !s.KeepSnapshots).Select(s => s.ProjectKey).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var byId = existing.ToDictionary(t => t.Id);
        var bySelector = existing.GroupBy(t => (t.ProjectKey, t.Selector)).ToDictionary(g => g.Key, g => g.First());
        var numbers = existing.GroupBy(t => t.ProjectKey).ToDictionary(g => g.Key, g => g.Select(t => t.Number).ToHashSet(), StringComparer.Ordinal);

        int added = 0, updated = 0, renumbered = 0, skipped = 0;
        var seen = new HashSet<Guid>();
        foreach (var item in valid)
        {
            var key = item.ProjectKey.Trim();
            if (!seen.Add(item.Id))
            {
                skipped++;
                continue;
            }
            var keepSnapshot = !noSnapshots.Contains(key);
            var local = byId.GetValueOrDefault(item.Id) ?? bySelector.GetValueOrDefault((key, item.Selector));
            if (local is not null)
            {
                // The same id under another project is not this test.
                if (local.ProjectKey != key || !IsNewer(item, local))
                {
                    skipped++;
                    continue;
                }
                if (!dryRun) ApplyImported(local, item, keepSnapshot);
                updated++;
                continue;
            }

            if (!numbers.TryGetValue(key, out var used)) numbers[key] = used = [];
            var number = item.Number > 0 && !used.Contains(item.Number) ? item.Number : used.Count == 0 ? 1 : used.Max() + 1;
            if (number != item.Number) renumbered++;
            used.Add(number);
            if (!dryRun)
            {
                var entity = new UnitTestCase { Id = item.Id };
                ApplyImported(entity, item, keepSnapshot);
                entity.Number = number;
                db.UnitTestCases.Add(entity);
            }
            added++;
        }

        if (!dryRun && added + updated > 0) await db.SaveChangesAsync(cancellationToken);
        return new UnitTestImport(added, updated, renumbered, skipped, warnings);
    }
}
