using AiThread.Common.Enums;
using AiThread.Data.Context;
using AiThread.Data.Messaging;
using AiThread.Service.Interfaces;
using AiThread.Service.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Background;

// Keeps thread state honest across restarts of a background instance. On start, threads left running by a process
// that ended abruptly (logoff, crash, forced stop) are closed as cancelled, and so are threads that were waiting on a
// planner question (the question lived in memory). While running, questions left unanswered too long are closed. On
// graceful stop, running threads are cancelled and given time to record that before the process exits.
public sealed class ThreadLifecycleWorker(
    IDbContextFactory<AiThreadDbContext> dbFactory,
    IThreadRegistry registry,
    IPendingQuestionStore questions,
    IEventBus eventBus,
    IActivityLogService activityLog,
    ILogger<ThreadLifecycleWorker> logger) : IHostedService
{
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan QuestionSweepInterval = TimeSpan.FromMinutes(5);

    private readonly CancellationTokenSource _stopping = new();
    private Task? _sweep;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await CloseLeftoversAsync(cancellationToken);
        _sweep = SweepQuestionsAsync(_stopping.Token);
    }

    private async Task CloseLeftoversAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var now = DateTime.UtcNow;

            var waiting = await db.Threads
                .Where(t => t.Status == ThreadStatus.AwaitingInput)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Status, ThreadStatus.Cancelled)
                    .SetProperty(t => t.Error, "AIT di-restart sebelum pertanyaan planner dijawab.")
                    .SetProperty(t => t.CompletedAt, now)
                    .SetProperty(t => t.UpdatedAt, now), cancellationToken);
            if (waiting > 0)
                activityLog.Warn("thread", $"{waiting} thread yang menunggu jawaban pertanyaan planner ditutup karena AIT di-restart.");

            var threads = await db.Threads
                .Where(t => t.Status == ThreadStatus.Pending || t.Status == ThreadStatus.Planning
                            || t.Status == ThreadStatus.Executing || t.Status == ThreadStatus.Verifying || t.Status == ThreadStatus.Wrapping)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Status, ThreadStatus.Cancelled)
                    .SetProperty(t => t.Error, "AIT berhenti saat thread ini berjalan.")
                    .SetProperty(t => t.CompletedAt, now)
                    .SetProperty(t => t.UpdatedAt, now), cancellationToken);
            if (threads == 0) return;

            await db.ThreadTasks
                .Where(t => t.Status == ThreadTaskStatus.Pending || t.Status == ThreadTaskStatus.Running)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Status, ThreadTaskStatus.Cancelled)
                    .SetProperty(t => t.CompletedAt, now)
                    .SetProperty(t => t.UpdatedAt, now), cancellationToken);

            activityLog.Warn("thread", $"{threads} thread yang tertinggal dari sesi AIT sebelumnya ditandai dibatalkan.");
        }
        catch (Exception ex)
        {
            // Database problems are already reported by the migration step and the dashboard.
            logger.LogWarning(ex, "Could not close threads left over from a previous run.");
        }
    }

    private async Task SweepQuestionsAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(QuestionSweepInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var expired = questions.TakeExpired();
                if (expired.Count == 0) continue;
                try
                {
                    var closed = await UnansweredQuestions.CloseAsync(dbFactory, eventBus, expired.Select(q => q.ThreadId).ToList(),
                        $"Pertanyaan planner tidak dijawab dalam {PendingQuestionStore.Lifetime.TotalHours:0} jam.");
                    if (closed > 0) activityLog.Info("thread", $"{closed} thread ditutup karena pertanyaan planner tidak dijawab.");
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not close threads with unanswered planner questions.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync();
        if (_sweep is not null) await _sweep;

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
