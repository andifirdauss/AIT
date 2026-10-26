using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
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
                           ?? throw new AppValidationException(Lang.T("Thread ini sedang berjalan; tunggu sampai selesai sebelum testing diulang.", "This thread is running; wait until it finishes before testing again."));
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
            if (thread.Status == ThreadStatus.RolledBack) throw new AppValidationException(Lang.T("Perubahan thread ini sudah dibatalkan (rollback), jadi tidak ada yang bisa dites.", "The changes of this thread were rolled back, so there is nothing to test."));
            if (thread.Status != ThreadStatus.Completed) throw new AppValidationException(Lang.T("Testing hanya bisa diulang untuk thread yang sudah selesai.", "Testing can only be repeated for a finished thread."));
            if (thread.ProjectId is not { } projectId) throw new AppValidationException(Lang.T("Project thread ini sudah dihapus.", "The project of this thread was deleted."));
            if (string.IsNullOrWhiteSpace(thread.PlanJson) || !ThreadPlan.TryParse(thread.PlanJson, out var plan, out _) || plan?.Test is not { IsRunnable: true })
                throw new AppValidationException(Lang.T("Thread ini tidak punya rencana testing UI/API: Auto Testing belum menyala saat thread dibuat, atau planner menilai perubahannya tidak bisa dites dari luar.", "This thread has no UI/API testing plan: Auto Testing was not on when the thread was created, or the planner judged its change could not be tested from outside."));

            var succeeded = rows.Where(r => r.Status == ThreadTaskStatus.Succeeded).GroupBy(r => r.TaskKey, StringComparer.OrdinalIgnoreCase).Select(g => g.Last()).ToList();
            // A thread that only tested (no task) tests the existing behaviour again.
            if (succeeded.Count == 0 && plan.Tasks.Count > 0) throw new AppValidationException(Lang.T("Tidak ada perubahan yang berhasil untuk dites.", "There are no successful changes to test."));
            var tester = (await nodeService.GetActiveAsync(NodeRole.Tester, cancellationToken)).FirstOrDefault()
                         ?? throw new AppValidationException(Lang.T("Belum ada node Tester aktif. Tambahkan di menu Node.", "No active Tester node yet. Add one in the Node menu."));
            var project = await projectService.GetContextAsync(projectId, cancellationToken)
                          ?? throw new AppValidationException(Lang.T("Project thread ini sudah dihapus.", "The project of this thread was deleted."));

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
