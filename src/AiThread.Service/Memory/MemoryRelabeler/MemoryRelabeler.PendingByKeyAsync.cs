using AiThread.Data.Memory;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Memory;

public sealed partial class MemoryRelabeler
{
    // The descriptions of the memories waiting for a label, per registry key (string.Empty: a project memory without a
    // key): their count orders the projects, their text names the files to map before labeling.
    private async Task<Dictionary<string, List<string>>> PendingByKeyAsync(IReadOnlyDictionary<Guid, (string? Key, string Name)> projects,
        CancellationToken cancellationToken)
    {
        var pending = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var record in (await ShortRecordsAsync()).Where(r => r.LabelVersion is null && r.ReviewReason is null))
            Add(pending, KeyOf(record.Type, record.ProjectKey, record.ProjectId, projects), record.Description);
        try
        {
            await using var db = await longStore.CreateAsync(cancellationToken);
            var rows = await db.LongMemories.AsNoTracking()
                .Where(m => m.LabelVersion == null && m.ReviewReason == null)
                .Select(m => new { m.Type, m.ProjectKey, m.ProjectId, m.Description })
                .ToListAsync(cancellationToken);
            foreach (var row in rows) Add(pending, KeyOf(row.Type, row.ProjectKey, row.ProjectId, projects), row.Description);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogStoreWarning(ex, "Could not count memories waiting for a label");
        }
        return pending;

        static void Add(Dictionary<string, List<string>> pending, string? key, string description)
        {
            if (!pending.TryGetValue(key ?? string.Empty, out var list)) pending[key ?? string.Empty] = list = [];
            list.Add(description);
        }
    }
}
