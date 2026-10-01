using AiThread.Common.Enums;
using AiThread.Data.Context;
using AiThread.Service.Interfaces;
using AiThread.Service.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Background;

// Keeps thread state honest across restarts of a background instance. On start, threads left running by a process
// that ended abruptly (logoff, crash, forced stop) are closed as cancelled. On graceful stop, running threads are
// cancelled and given time to record that before the process exits.
public sealed class ThreadLifecycleWorker(
    IDbContextFactory<AiThreadDbContext> dbFactory,
    ThreadRegistry registry,
    IActivityLogService activityLog,
    ILogger<ThreadLifecycleWorker> logger) : IHostedService
{
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(15);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var now = DateTime.UtcNow;

            var threads = await db.Threads
                .Where(t => t.Status == ThreadStatus.Pending || t.Status == ThreadStatus.Planning
                            || t.Status == ThreadStatus.Executing || t.Status == ThreadStatus.Verifying || t.Status == ThreadStatus.Wrapping)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Status, ThreadStatus.Cancelled)
                    .SetProperty(t => t.Error, "AiThread berhenti saat thread ini berjalan.")
                    .SetProperty(t => t.CompletedAt, now)
                    .SetProperty(t => t.UpdatedAt, now), cancellationToken);
            if (threads == 0) return;

            await db.ThreadTasks
                .Where(t => t.Status == ThreadTaskStatus.Pending || t.Status == ThreadTaskStatus.Running)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Status, ThreadTaskStatus.Cancelled)
                    .SetProperty(t => t.CompletedAt, now)
                    .SetProperty(t => t.UpdatedAt, now), cancellationToken);

            activityLog.Warn("thread", $"{threads} thread yang tertinggal dari sesi AiThread sebelumnya ditandai dibatalkan.");
        }
        catch (Exception ex)
        {
            // Database problems are already reported by the migration step and the dashboard.
            logger.LogWarning(ex, "Could not close threads left over from a previous run.");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Dashboard and Telegram threads already follow ApplicationStopping; Claude Code threads follow their request.
        var cancelled = registry.CancelAll();
        if (cancelled == 0) return;

        logger.LogInformation("Shutting down: cancelled {Count} running thread(s).", cancelled);
        try
        {
            if (!await registry.WaitForIdleAsync(DrainTimeout, cancellationToken))
                logger.LogWarning("{Count} thread(s) did not finish cancelling before shutdown.", registry.ActiveCount);
        }
        catch (OperationCanceledException)
        {
            // Host shutdown timeout reached.
        }
    }
}
