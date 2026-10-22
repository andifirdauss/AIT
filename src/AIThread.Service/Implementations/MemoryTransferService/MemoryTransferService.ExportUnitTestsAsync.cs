using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryTransferService
{
    // The unit test catalog of the exported projects (every project without a project filter), so a team without a
    // shared store still shares it. Snapshots go along unless the project turned them off.
    private async Task<IReadOnlyList<UnitTestExportItem>?> ExportUnitTestsAsync(MemoryQuery filter, LocalProject? scope, CancellationToken cancellationToken)
    {
        if (!filter.IncludeUnitTests || filter.Type == MemoryType.Knowledge || !string.IsNullOrWhiteSpace(filter.Search)) return null;
        if (scope is not null && scope.Key is null) return null;

        await using var db = await longStore.CreateUnitTestsAsync(cancellationToken);
        var query = db.UnitTestCases.AsNoTracking();
        var settings = db.UnitTestSettings.AsNoTracking();
        if (scope?.Key is { } key)
        {
            query = query.Where(t => t.ProjectKey == key);
            settings = settings.Where(s => s.ProjectKey == key);
        }
        var tests = await query.OrderBy(t => t.ProjectKey).ThenBy(t => t.Number).ToListAsync(cancellationToken);
        if (tests.Count == 0) return null;
        var noSnapshots = (await settings.Where(s => !s.KeepSnapshots).Select(s => s.ProjectKey).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        return tests.Select(t => ToExport(t, !noSnapshots.Contains(t.ProjectKey))).ToList();
    }
}
