using AIThread.Common.Entities;
using AIThread.Data.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectService
{
    // Memory per project: everything under its key (from any developer) plus older rows that only carry its id.
    // Long memory may live in another database, so an outage there only hides that part of the count.
    private async Task<Dictionary<Guid, int>> MemoryCountsAsync(IReadOnlyList<Project> projects, string? onlyKey, CancellationToken cancellationToken)
    {
        var byKey = new Dictionary<string, int>();
        var byId = new Dictionary<Guid, int>();
        void Count(string? key, Guid? id)
        {
            if (key is not null) byKey[key] = byKey.GetValueOrDefault(key) + 1;
            else if (id is { } value) byId[value] = byId.GetValueOrDefault(value) + 1;
        }

        if (shortMemoryStore.IsConnected)
        {
            try
            {
                foreach (var record in await shortMemoryStore.GetAllAsync()) Count(record.ProjectKey, record.ProjectId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not read short memories.");
            }
        }

        try
        {
            await using var db = await longMemoryStore.CreateAsync(cancellationToken);
            var query = db.LongMemories.AsNoTracking().Where(m => m.ProjectKey != null || m.ProjectId != null);
            if (onlyKey is { Length: > 0 })
            {
                var ids = projects.Select(p => p.Id).ToList();
                query = query.Where(m => m.ProjectKey == onlyKey || (m.ProjectKey == null && m.ProjectId.HasValue && ids.Contains(m.ProjectId.Value)));
            }
            var groups = await query
                .GroupBy(m => new { m.ProjectKey, m.ProjectId })
                .Select(g => new { g.Key.ProjectKey, g.Key.ProjectId, Count = g.Count() })
                .ToListAsync(cancellationToken);
            foreach (var group in groups)
            {
                if (group.ProjectKey is not null) byKey[group.ProjectKey] = byKey.GetValueOrDefault(group.ProjectKey) + group.Count;
                else if (group.ProjectId is { } id) byId[id] = byId.GetValueOrDefault(id) + group.Count;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogStoreWarning(ex, "Could not count long memories");
        }

        return projects.ToDictionary(
            p => p.Id,
            p => (string.IsNullOrEmpty(p.ProjectKey) ? 0 : byKey.GetValueOrDefault(p.ProjectKey)) + byId.GetValueOrDefault(p.Id));
    }
}
