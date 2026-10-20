using AiThread.Common.Enums;
using AiThread.Data.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Memory;

public sealed partial class MemoryRelabeler
{
    // Claims up to BatchSize rows (long memory first), only of onlyKey when given: a row is only taken when its attempt
    // count is still the one read, so two machines never take the same row.
    private async Task<List<Candidate>> ClaimAsync(IReadOnlyDictionary<Guid, (string? Key, string Name)> projects, string? onlyKey,
        CancellationToken cancellationToken)
    {
        var claimed = new List<Candidate>();
        try
        {
            await using var db = await longStore.CreateAsync(cancellationToken);
            var query = db.LongMemories.AsNoTracking()
                .Where(m => m.LabelVersion == null && m.ReviewReason == null && (m.RelabelAttempts == null || m.RelabelAttempts < MaxAttempts));
            if (onlyKey is not null) query = query.Where(m => m.ProjectKey == onlyKey);
            var rows = await query
                .OrderBy(m => m.ProjectKey).ThenBy(m => m.CreatedAt)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            foreach (var row in rows)
            {
                var attempts = row.RelabelAttempts;
                var id = row.Id;
                var taken = await db.LongMemories
                    .Where(m => m.Id == id && m.LabelVersion == null && m.RelabelAttempts == attempts)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.RelabelAttempts, (attempts ?? 0) + 1), cancellationToken);
                if (taken == 1)
                    claimed.Add(new Candidate(row.Id, MemoryLevel.Long, row.Type, KeyOf(row.Type, row.ProjectKey, row.ProjectId, projects),
                        row.ProjectName, row.Label, row.Description, (attempts ?? 0) + 1));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogStoreWarning(ex, "Could not claim long memories for relabeling");
        }

        if (claimed.Count < BatchSize)
        {
            foreach (var record in (await ShortRecordsAsync())
                         .Where(r => r.LabelVersion is null && r.ReviewReason is null && (r.RelabelAttempts ?? 0) < MaxAttempts
                                     && (onlyKey is null || KeyOf(r.Type, r.ProjectKey, r.ProjectId, projects) == onlyKey))
                         .Take(BatchSize - claimed.Count))
            {
                var attempts = (record.RelabelAttempts ?? 0) + 1;
                try
                {
                    await shortStore.SaveAsync(record with { RelabelAttempts = attempts });
                    claimed.Add(new Candidate(record.Id, MemoryLevel.Short, record.Type, KeyOf(record.Type, record.ProjectKey, record.ProjectId, projects),
                        record.ProjectName, record.Label, record.Description, attempts));
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not claim short memory {Id} for relabeling.", record.Id);
                }
            }
        }
        return claimed;
    }
}
