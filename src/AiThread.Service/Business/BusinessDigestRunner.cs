using System.Collections.Concurrent;
using AiThread.Common.DTOs;
using AiThread.Data.Context;
using AiThread.Data.Messaging;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Business;

// Runs the business wiki writer over the threads waiting for it: for one project (button "Perbarui sekarang") or for
// every project (BusinessMemoryWorker). One run at a time on this machine; other developers' AiThread write to the
// same pages safely through the page version check.
public sealed class BusinessDigestRunner(
    IDbContextFactory<AiThreadDbContext> dbFactory,
    BusinessDigestCollector collector,
    BusinessMemoryWriter writer,
    IEventBus eventBus,
    IActivityLogService activityLog,
    ILogger<BusinessDigestRunner> logger)
{
    // A run stops after this many batches; the rest waits for the next run.
    private const int MaxBatchesPerRun = 5;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, byte> _running = new();

    public bool IsRunning(string projectKey) => _running.ContainsKey(projectKey);

    public async Task<BusinessDigestResultDto> RunProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        string key, name;
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
                          ?? throw new AppValidationException("Project tidak ditemukan.");
            (key, name) = (project.ProjectKey, project.Name);
        }
        if (string.IsNullOrEmpty(key)) throw new AppValidationException("Project belum punya key; buka lagi menu Project sebentar lagi.");

        if (!await _gate.WaitAsync(TimeSpan.Zero, cancellationToken))
            throw new AppValidationException("Pembaruan business memory sedang berjalan. Tunggu sampai selesai.");
        try
        {
            if (await collector.CountPendingAsync(key, cancellationToken) == 0)
                return new BusinessDigestResultDto(0, 0, 0, 0, ["Tidak ada thread baru yang belum dirangkum untuk project ini."]);
            return await RunKeyAsync(key, name, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RunAllAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var (key, name) in await collector.PendingProjectsAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await RunKeyAsync(key, name, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // One project's failure (no model, router down) must not stop the others.
                    logger.LogWarning(ex, "Business memory update of {ProjectKey} failed.", key);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<BusinessDigestResultDto> RunKeyAsync(string key, string name, CancellationToken cancellationToken)
    {
        _running[key] = 0;
        Publish("running", key);
        int threads = 0, created = 0, updated = 0, images = 0;
        var warnings = new List<string>();
        try
        {
            activityLog.Info("business", $"Business memory {name}: merangkum thread baru ke wiki bisnis…");
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

            var summary = $"Business memory {name}: {threads} thread dirangkum — {created} halaman baru, {updated} halaman diperbarui" +
                          (images > 0 ? $", {images} screenshot." : ".");
            if (warnings.Count > 0) activityLog.Warn("business", summary + " " + string.Join(" ", warnings.Take(3)));
            else activityLog.Success("business", summary);
            return new BusinessDigestResultDto(threads, created, updated, images, warnings);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            activityLog.Warn("business", $"Business memory {name} gagal diperbarui: {ex.Message}");
            throw;
        }
        finally
        {
            _running.TryRemove(key, out _);
            Publish("finished", key);
        }
    }

    private void Publish(string action, string projectKey) =>
        eventBus.Publish(AppEvent.Create(AppEventTypes.BusinessChanged, new { action, projectKey }));
}
