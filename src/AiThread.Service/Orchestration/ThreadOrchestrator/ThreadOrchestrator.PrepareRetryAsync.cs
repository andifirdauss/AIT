using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // Registers the thread as running before anything else, so a second retry or a rollback cannot start meanwhile.
    private async Task<PreparedRetry> PrepareRetryAsync(Guid threadId, CancellationToken runToken, CancellationToken cancellationToken)
    {
        var cancellation = registry.TryRegister(threadId, runToken)
                           ?? throw new AppValidationException("Thread ini sedang berjalan; tunggu sampai selesai sebelum mengulang.");
        try
        {
            ThreadRun? thread;
            List<ThreadTask> rows;
            await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
            {
                thread = await db.Threads.AsNoTracking().FirstOrDefaultAsync(t => t.Id == threadId, cancellationToken);
                rows = await db.ThreadTasks.AsNoTracking().Where(t => t.ThreadRunId == threadId).OrderBy(t => t.Order).ToListAsync(cancellationToken);
            }

            if (thread is null) throw new AppValidationException("Thread tidak ditemukan.");
            var refusal = thread.Status switch
            {
                ThreadStatus.Completed or ThreadStatus.Failed or ThreadStatus.Cancelled => null,
                ThreadStatus.RolledBack => "Perubahan thread ini sudah dibatalkan (rollback), jadi tidak bisa diulang. Mulai thread baru.",
                ThreadStatus.AwaitingInput => "Thread ini masih menunggu jawaban pertanyaan planner.",
                _ => "Thread ini belum selesai."
            };
            if (refusal is not null) throw new AppValidationException(refusal);
            if (thread.ProjectId is not { } projectId) throw new AppValidationException("Project thread ini sudah dihapus.");
            if (string.IsNullOrWhiteSpace(thread.PlanJson) || !ThreadPlan.TryParse(thread.PlanJson, out var plan, out _) || plan is null || plan.Tasks.Count == 0)
                throw new AppValidationException("Thread ini tidak punya rencana dengan tugas eksekusi, jadi tidak ada yang bisa diulang.");

            var succeeded = rows.Where(r => r.Status == ThreadTaskStatus.Succeeded).Select(r => r.TaskKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var retryTasks = plan.Tasks.Where(t => !succeeded.Contains(t.Id)).ToList();
            if (retryTasks.Count == 0) throw new AppValidationException("Semua tugas rencana sudah berhasil; tidak ada yang perlu diulang.");

            var project = await projectService.GetContextAsync(projectId, cancellationToken)
                          ?? throw new AppValidationException("Project thread ini sudah dihapus.");
            return new PreparedRetry(thread, project, plan, rows, retryTasks, cancellation);
        }
        catch
        {
            registry.Complete(threadId);
            throw;
        }
    }
}
