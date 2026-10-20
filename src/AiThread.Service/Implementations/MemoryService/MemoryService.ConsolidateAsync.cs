using AiThread.Data.Memory;
using AiThread.Service.Interfaces;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryService
{
    public async Task<int> ConsolidateAsync(CancellationToken cancellationToken = default)
    {
        if (!shortStore.IsConnected) return 0;

        var cutoff = DateTime.UtcNow.AddHours(-options.Value.ShortMemoryHours);
        var due = await shortStore.GetCreatedBeforeAsync(cutoff);
        var moved = 0;
        foreach (var record in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await InsertLongAsync(record, cancellationToken);
                await shortStore.DeleteAsync(record.Id);
                moved++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (StoreErrors.IsUnreachable(ex))
                {
                    // Every record would fail the same way; they stay in short memory until the next round.
                    LongStoreFailed(ex, $"Memory consolidation postponed ({due.Count - moved} due)");
                    break;
                }
                logger.LogWarning(ex, "Could not consolidate short memory {Id}", record.Id);
            }
        }

        if (moved > 0)
        {
            Publish("consolidated", null);
            activityLog.Info("memory", $"{moved} short memory (> {options.Value.ShortMemoryHours} jam) dipindah ke long memory.");
        }
        return moved;
    }
}
