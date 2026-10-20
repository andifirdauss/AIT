using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryService
{
    // The business wiki may live in another (shared) database; an outage there only hides the pages.
    private async Task<IReadOnlyList<MemoryIndexEntry>> BusinessIndexAsync(ProjectScope? scope, CancellationToken cancellationToken)
    {
        if (scope?.Key is not { } key) return [];
        try
        {
            await using var db = await businessStore.CreateAsync(cancellationToken);
            var rows = await db.BusinessPages.AsNoTracking()
                .Where(p => p.ProjectKey == key)
                .Select(p => new { p.Id, p.Module, p.Title, p.Labels, p.CreatedAt, p.UpdatedAt })
                .ToListAsync(cancellationToken);
            return rows.Select(p => new MemoryIndexEntry(p.Id, MemoryLevel.Business, MemoryType.Business,
                $"{p.Module} — {p.Title} (business wiki): {p.Labels.Replace(",", ", ")}", p.UpdatedAt ?? p.CreatedAt, p.Module)).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read the business wiki index from {Store}.", businessStore.Description);
            return [];
        }
    }
}
