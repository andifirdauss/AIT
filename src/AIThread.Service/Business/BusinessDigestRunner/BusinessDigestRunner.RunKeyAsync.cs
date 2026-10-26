using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Business;

public sealed partial class BusinessDigestRunner
{
    private async Task<BusinessDigestResultDto> RunKeyAsync(string key, string name, CancellationToken cancellationToken)
    {
        _running[key] = 0;
        Publish("running", key);
        int threads = 0, created = 0, updated = 0, images = 0;
        var warnings = new List<string>();
        try
        {
            activityLog.Info("business", Lang.T($"Business memory {name}: merangkum thread baru ke wiki bisnis…", $"Business memory {name}: summarizing new threads into the business wiki…"));
            for (var batch = 0; batch < MaxBatchesPerRun; batch++)
            {
                var digests = await collector.NextBatchAsync(key, cancellationToken);
                if (digests.Count == 0) break;

                var result = await writer.WriteAsync(key, name, digests, cancellationToken);
                await collector.MarkDigestedAsync(result.DigestedThreads, cancellationToken);
                threads += result.DigestedThreads.Count;
                created += result.PagesCreated;
                updated += result.PagesUpdated;
                images += result.Images;
                warnings.AddRange(result.Warnings);

                // Nothing could be saved: the same threads would fail again right away.
                if (result.DigestedThreads.Count == 0) break;
            }

            var summary = Lang.T($"Business memory {name}: {threads} thread dirangkum — {created} halaman baru, {updated} halaman diperbarui", $"Business memory {name}: {threads} threads summarized — {created} new pages, {updated} pages updated") +
                          (images > 0 ? $", {images} screenshot." : ".");
            if (warnings.Count > 0) activityLog.Warn("business", summary + " " + string.Join(" ", warnings.Take(3)));
            else activityLog.Success("business", summary);
            return new BusinessDigestResultDto(threads, created, updated, images, warnings);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            activityLog.Warn("business", Lang.T($"Business memory {name} gagal diperbarui: {ex.Message}", $"Business memory {name} could not be updated: {ex.Message}"));
            throw;
        }
        finally
        {
            _running.TryRemove(key, out _);
            Publish("finished", key);
        }
    }
}
