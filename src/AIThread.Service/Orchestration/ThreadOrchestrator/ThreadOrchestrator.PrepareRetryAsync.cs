using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // Registers the thread as running before anything else, so a second retry or a rollback cannot start meanwhile.
    private async Task<PreparedRetry> PrepareRetryAsync(Guid threadId, CancellationToken runToken, CancellationToken cancellationToken)
    {
        var cancellation = registry.TryRegister(threadId, runToken)
                           ?? throw new AppValidationException(Lang.T("Thread ini sedang berjalan; tunggu sampai selesai sebelum mengulang.", "This thread is running; wait until it finishes before retrying."));
        try
        {
            ThreadRun? thread;
            List<ThreadTask> rows;
            await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
            {
                thread = await db.Threads.AsNoTracking().FirstOrDefaultAsync(t => t.Id == threadId, cancellationToken);
                rows = await db.ThreadTasks.AsNoTracking().Where(t => t.ThreadRunId == threadId).OrderBy(t => t.Order).ToListAsync(cancellationToken);
            }

            if (thread is null) throw new AppValidationException(Lang.T("Thread tidak ditemukan.", "Thread not found."));
            var refusal = thread.Status switch
            {
                ThreadStatus.Completed or ThreadStatus.Failed or ThreadStatus.Cancelled => null,
                ThreadStatus.RolledBack => Lang.T("Perubahan thread ini sudah dibatalkan (rollback), jadi tidak bisa diulang. Mulai thread baru.", "The changes of this thread were rolled back, so it cannot be retried. Start a new thread."),
                ThreadStatus.AwaitingInput => Lang.T("Thread ini masih menunggu jawaban pertanyaan planner.", "This thread is still waiting for the answer to the planner's question."),
                _ => Lang.T("Thread ini belum selesai.", "This thread has not finished yet.")
            };
            if (refusal is not null) throw new AppValidationException(refusal);
            if (thread.ProjectId is not { } projectId) throw new AppValidationException(Lang.T("Project thread ini sudah dihapus.", "The project of this thread was deleted."));
            if (string.IsNullOrWhiteSpace(thread.PlanJson) || !ThreadPlan.TryParse(thread.PlanJson, out var plan, out _) || plan is null || plan.Tasks.Count == 0)
                throw new AppValidationException(Lang.T("Thread ini tidak punya rencana dengan tugas eksekusi, jadi tidak ada yang bisa diulang.", "This thread has no plan with execution tasks, so there is nothing to retry."));

            var succeeded = rows.Where(r => r.Status == ThreadTaskStatus.Succeeded).Select(r => r.TaskKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var retryTasks = plan.Tasks.Where(t => !succeeded.Contains(t.Id)).ToList();
            if (retryTasks.Count == 0) throw new AppValidationException(Lang.T("Semua tugas rencana sudah berhasil; tidak ada yang perlu diulang.", "Every task of the plan already succeeded; nothing needs to be retried."));

            var project = await projectService.GetContextAsync(projectId, cancellationToken)
                          ?? throw new AppValidationException(Lang.T("Project thread ini sudah dihapus.", "The project of this thread was deleted."));
            return new PreparedRetry(thread, project, plan, rows, retryTasks, cancellation);
        }
        catch
        {
            registry.Complete(threadId);
            throw;
        }
    }
}
