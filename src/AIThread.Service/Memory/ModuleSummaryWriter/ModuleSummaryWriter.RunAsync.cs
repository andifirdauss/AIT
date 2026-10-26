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
            TrackProgress(pending.Sum(p => p.Facts) + await ShortFactsAsync());
            if (pending.Count == 0) return 0;
            var withFacts = !pending.Any(p => p.Facts > 0) || await BackupAsync(cancellationToken);

            var taken = pending.Where(p => withFacts || p.Changes + p.ExtraSummaries > 0).Take(MaxModulesPerRun).ToList();
            var migrating = withFacts && taken.Any(p => p.Facts > 0);
            (_done, _total) = (0, taken.Count);
            var written = 0;
            var migrated = 0;
            foreach (var item in taken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _module = item.Module;
                if (migrating) PublishStatus();
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
                    migrated += folded.Facts;
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
                finally
                {
                    _done++;
                }
            }

            _lastRunAt = DateTime.UtcNow;
            // Done: clears the count once nothing waits any more.
            if (migrating) TrackProgress((await PendingAsync(db, withFacts: true, cancellationToken)).Sum(p => p.Facts) + await ShortFactsAsync());
            if (migrated > 0) _lastMessage = $"{migrated} memory lama dimigrasi ke ringkasan modul pada putaran terakhir.";
            return written;
        }
        catch (Exception ex) when (ex is not OperationCanceledException && (ex is MemoryAiUnavailableException || StoreErrors.IsUnreachable(ex)))
        {
            logger.LogInformation(ex, "Module summaries postponed.");
            _lastMessage = ex is MemoryAiUnavailableException
                ? "Menunggu node Tools, Wrapper, atau Planner yang aktif."
                : "Long memory tidak dapat dihubungi; migrasi dilanjutkan putaran berikutnya.";
            return 0;
        }
        finally
        {
            var wasRunning = _total > 0;
            (_module, _done, _total) = (null, 0, 0);
            _gate.Release();
            if (wasRunning) PublishStatus();
        }
    }
}
