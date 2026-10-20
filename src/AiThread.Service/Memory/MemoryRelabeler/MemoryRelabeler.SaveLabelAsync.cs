using AiThread.Common.Enums;
using AiThread.Common.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Memory;

public sealed partial class MemoryRelabeler
{
    private async Task<bool> SaveLabelAsync(Candidate candidate, NewLabel label, CancellationToken cancellationToken)
    {
        var composed = MemoryLabels.Compose(MemoryLabels.Display(label.Area, label.Module, label.Layers, label.Kind, label.ExtraModules), label.Keywords);
        try
        {
            if (candidate.Level == MemoryLevel.Short)
            {
                if (await shortStore.GetAsync(candidate.Id) is not { } record) return false;
                await shortStore.SaveAsync(record with
                {
                    Label = composed, LegacyLabel = record.LegacyLabel ?? record.Label, Module = label.Module,
                    ExtraModules = MemoryLabels.JoinCsv(label.ExtraModules), Layers = MemoryLabels.LayersCsv(label.Layers),
                    Kind = label.Kind, Area = label.Area, Keywords = MemoryLabels.JoinCsv(label.Keywords), LabelVersion = MemoryLabels.CurrentVersion,
                    ReviewReason = null
                });
                return true;
            }

            await using var db = await longStore.CreateAsync(cancellationToken);
            var row = await db.LongMemories.FirstOrDefaultAsync(m => m.Id == candidate.Id && m.LabelVersion == null, cancellationToken);
            if (row is null) return false;
            row.LegacyLabel ??= row.Label;
            row.Label = composed;
            row.Module = label.Module;
            row.ExtraModules = MemoryLabels.JoinCsv(label.ExtraModules);
            row.Layers = MemoryLabels.LayersCsv(label.Layers);
            row.Kind = label.Kind;
            row.Area = label.Area;
            row.Keywords = MemoryLabels.JoinCsv(label.Keywords);
            row.LabelVersion = MemoryLabels.CurrentVersion;
            row.ReviewReason = null;
            row.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not save the new label of memory {Id}.", candidate.Id);
            return false;
        }
    }
}
