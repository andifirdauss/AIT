using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // Registers the thread as running first, so a retry, a rollback or a second retest cannot start meanwhile. runToken
    // stops the run: the application stopping, or the chat request it is streamed to.
    private async Task<PreparedRetest> PrepareRetestAsync(Guid threadId, CancellationToken runToken, CancellationToken cancellationToken)
    {
        var cancellation = registry.TryRegister(threadId, runToken)
                           ?? throw new AppValidationException("Thread ini sedang berjalan; tunggu sampai selesai sebelum testing diulang.");
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
            if (thread.Status == ThreadStatus.RolledBack) throw new AppValidationException("Perubahan thread ini sudah dibatalkan (rollback), jadi tidak ada yang bisa dites.");
            if (thread.Status != ThreadStatus.Completed) throw new AppValidationException("Testing hanya bisa diulang untuk thread yang sudah selesai.");
            if (thread.ProjectId is not { } projectId) throw new AppValidationException("Project thread ini sudah dihapus.");
            if (string.IsNullOrWhiteSpace(thread.PlanJson) || !ThreadPlan.TryParse(thread.PlanJson, out var plan, out _) || plan?.Test is not { IsRunnable: true })
                throw new AppValidationException("Thread ini tidak punya rencana testing UI/API: Auto Testing belum menyala saat thread dibuat, atau planner menilai perubahannya tidak bisa dites dari luar.");

            var succeeded = rows.Where(r => r.Status == ThreadTaskStatus.Succeeded).GroupBy(r => r.TaskKey, StringComparer.OrdinalIgnoreCase).Select(g => g.Last()).ToList();
            // A thread that only tested (no task) tests the existing behaviour again.
            if (succeeded.Count == 0 && plan.Tasks.Count > 0) throw new AppValidationException("Tidak ada perubahan yang berhasil untuk dites.");
            var tester = (await nodeService.GetActiveAsync(NodeRole.Tester, cancellationToken)).FirstOrDefault()
                         ?? throw new AppValidationException("Belum ada node Tester aktif. Tambahkan di menu Node.");
            var project = await projectService.GetContextAsync(projectId, cancellationToken)
                          ?? throw new AppValidationException("Project thread ini sudah dihapus.");

            // The tester sees every change that succeeded, the verification's fix tasks included.
            var planIds = plan.Tasks.Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var tasks = plan.Tasks.Concat(succeeded.Where(r => !planIds.Contains(r.TaskKey)).Select(ToPlanTask)).ToList();
            var fullPlan = new ThreadPlan { Summary = plan.Summary, Answer = plan.Answer, Contract = plan.Contract, Tasks = tasks, Test = plan.Test };
            var execution = new ExecutionSummary(succeeded.Select(ToResult).ToList(), 0, 0, 0);
            return new PreparedRetest(thread, project, fullPlan, execution, tester, cancellation);
        }
        catch
        {
            registry.Complete(threadId);
            throw;
        }
    }
}
