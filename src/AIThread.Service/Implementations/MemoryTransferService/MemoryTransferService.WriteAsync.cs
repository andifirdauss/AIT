using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Memory;
using AIThread.Data.Cache;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryTransferService
{
    private async Task WriteAsync(List<Prepared> adds, List<Prepared> updates, bool shortConnected, Dictionary<Guid, ShortMemoryRecord> existingShort,
        CancellationToken cancellationToken)
    {
        var toLong = new List<Prepared>();
        foreach (var item in adds)
        {
            if (item.Level == MemoryLevel.Short && shortConnected) await shortStore.SaveAsync(ToRecord(item));
            else toLong.Add(item);
        }

        foreach (var item in updates.Where(u => existingShort.ContainsKey(u.Id)))
        {
            var label = item.Labels;
            await shortStore.SaveAsync(existingShort[item.Id] with
            {
                Type = item.Type,
                ProjectId = item.ProjectId,
                ProjectKey = item.Key,
                ProjectName = item.Name,
                Label = item.Label,
                Description = item.Description,
                Module = label?.Module,
                ExtraModules = MemoryLabels.JoinCsv(label?.ExtraModules),
                Layers = MemoryLabels.LayersCsv(label?.Layers),
                Kind = label?.Kind,
                Area = label?.Area,
                Keywords = MemoryLabels.JoinCsv(label?.Keywords),
                LabelVersion = label is null ? null : MemoryLabels.CurrentVersion,
                LegacyLabel = item.LegacyLabel,
                ReviewReason = null,
                RelabelAttempts = null
            });
        }

        var longUpdates = updates.Where(u => !existingShort.ContainsKey(u.Id)).ToDictionary(u => u.Id);
        if (toLong.Count == 0 && longUpdates.Count == 0) return;

        await using var db = await longStore.CreateAsync(cancellationToken);
        foreach (var batch in toLong.Chunk(BatchSize))
        {
            db.LongMemories.AddRange(batch.Select(item => new LongMemory
            {
                Id = item.Id,
                ProjectId = item.ProjectId,
                ProjectKey = item.Key,
                ProjectName = item.Name,
                Type = item.Type,
                Label = item.Label,
                Description = item.Description,
                Module = item.Labels?.Module,
                ExtraModules = MemoryLabels.JoinCsv(item.Labels?.ExtraModules),
                Layers = MemoryLabels.LayersCsv(item.Labels?.Layers),
                Kind = item.Labels?.Kind,
                Area = item.Labels?.Area,
                Keywords = MemoryLabels.JoinCsv(item.Labels?.Keywords),
                LabelVersion = item.Labels is null ? null : MemoryLabels.CurrentVersion,
                LegacyLabel = item.LegacyLabel,
                SourceThreadId = item.SourceThreadId,
                GitAuthor = item.GitAuthor,
                GitBranch = item.GitBranch,
                GitCommit = item.GitCommit,
                CreatedAt = item.CreatedAt,
                PromotedAt = item.PromotedAt ?? DateTime.UtcNow,
                HitCount = item.HitCount,
                LastAccessedAt = item.LastAccessedAt
            }));
            await db.SaveChangesAsync(cancellationToken);
        }

        foreach (var batch in longUpdates.Keys.Chunk(BatchSize))
        {
            var rows = await db.LongMemories.Where(m => batch.Contains(m.Id)).ToListAsync(cancellationToken);
            foreach (var row in rows)
            {
                var item = longUpdates[row.Id];
                row.Type = item.Type;
                row.ProjectId = item.ProjectId;
                row.ProjectKey = item.Key;
                row.ProjectName = item.Name;
                row.Label = item.Label;
                row.Description = item.Description;
                row.Module = item.Labels?.Module;
                row.ExtraModules = MemoryLabels.JoinCsv(item.Labels?.ExtraModules);
                row.Layers = MemoryLabels.LayersCsv(item.Labels?.Layers);
                row.Kind = item.Labels?.Kind;
                row.Area = item.Labels?.Area;
                row.Keywords = MemoryLabels.JoinCsv(item.Labels?.Keywords);
                row.LabelVersion = item.Labels is null ? null : MemoryLabels.CurrentVersion;
                row.LegacyLabel = item.LegacyLabel;
                // A file from an older version has no git origin; the one known here stays.
                row.GitAuthor = item.GitAuthor ?? row.GitAuthor;
                row.GitBranch = item.GitBranch ?? row.GitBranch;
                row.GitCommit = item.GitCommit ?? row.GitCommit;
                row.ReviewReason = null;
                row.RelabelAttempts = null;
                row.UpdatedAt = DateTime.UtcNow;
            }
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
