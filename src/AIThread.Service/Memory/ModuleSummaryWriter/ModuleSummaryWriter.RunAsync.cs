using AIThread.Common.DTOs;
using AIThread.Data.Memory;
using AIThread.Service.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Memory;

public sealed partial class ModuleSummaryWriter
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(TimeSpan.Zero, cancellationToken)) return 0;
        try
        {
            if (!await ai.IsAvailableAsync(cancellationToken)) return 0;

            await using var db = await longStore.CreateAsync(cancellationToken);
            var pending = await PendingAsync(db, withFacts: true, cancellationToken);
            if (pending.Count == 0) return 0;
            var withFacts = !pending.Any(p => p.Facts > 0) || await BackupAsync(cancellationToken);

            var written = 0;
            foreach (var item in pending.Where(p => withFacts || p.Changes + p.ExtraSummaries > 0).Take(MaxModulesPerRun))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    // A fresh context per module: a failed save must not leave its changes tracked for the next one.
                    await using var moduleDb = await longStore.CreateAsync(cancellationToken);
                    if (await FoldAsync(moduleDb, item, withFacts, cancellationToken) is not { } folded)
                    {
                        logger.LogInformation("Summary of module {Module} ({Key}) not updated: nothing to fold or no usable answer.", item.Module, item.Key);
                        continue;
                    }
                    written++;
                    var parts = new List<string>();
                    if (folded.Changes > 0) parts.Add($"{folded.Changes} catatan perubahan");
                    if (folded.Facts > 0) parts.Add($"{folded.Facts} memory lama dimigrasi");
                    activityLog.Info("memory", $"Ringkasan modul `{folded.Summary.Module}` ({folded.Summary.ProjectName ?? item.Key}) diperbarui" +
                        (parts.Count > 0 ? $": {string.Join(", ", parts)}." : "."));
                    eventBus.Publish(AppEvent.Create(AppEventTypes.MemoryChanged, new { id = folded.Summary.Id, action = "summary" }));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (StoreErrors.IsUnreachable(ex)) throw;
                    // Another machine sharing the store may have written the same summary first: the next pass reads it.
                    logger.LogWarning(ex, "Could not update the summary of module {Module} ({Key}).", item.Module, item.Key);
                }
            }
            return written;
        }
        catch (Exception ex) when (ex is not OperationCanceledException && (ex is MemoryAiUnavailableException || StoreErrors.IsUnreachable(ex)))
        {
            logger.LogInformation(ex, "Module summaries postponed.");
            return 0;
        }
        finally
        {
            _gate.Release();
        }
    }
}
