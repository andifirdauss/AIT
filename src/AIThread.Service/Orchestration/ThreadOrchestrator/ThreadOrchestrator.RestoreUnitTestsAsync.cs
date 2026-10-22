using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.Models;
using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // Missing unit tests come back before planning. What needs a model is written by executors without a journal: the
    // restore is not part of the request, so undoing the thread leaves the restored tests in place.
    private async Task RestoreUnitTestsAsync(UnitTestSession unitTests, IReadOnlyList<AiNode> executors, WorkspaceScope scope, RuntimeSettings settings,
        ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        var tasks = await unitTestStage.RestoreAsync(unitTests, recorder, progress, cancellationToken);
        if (tasks.Count == 0) return;
        if (executors.Count == 0)
        {
            await progress.StepAsync("🟡 Belum ada node Executor aktif untuk menulis ulang test yang hilang", cancellationToken, last: true);
            return;
        }

        // Before the plan's tasks in the task list.
        var restorePlan = new ThreadPlan { Summary = "Pemulihan unit test dari katalog", Tasks = tasks.ToList() };
        var entities = await recorder.CreateTasksAsync(restorePlan, firstOrder: -tasks.Count);
        var execution = await executionEngine.RunAsync(new ExecutionRequest(restorePlan, entities, executors, scope, settings, Journal: null, Nested: true),
            recorder, progress, cancellationToken);
        await recorder.UpdateThreadAsync(t => t.ExecutorTokens += execution.Tokens);
        var done = execution.Results.Count(r => r.Status == ThreadTaskStatus.Succeeded);
        recorder.Log(done == tasks.Count ? ActivityLevel.Success : ActivityLevel.Warning, $"Pemulihan unit test oleh executor: {done}/{tasks.Count} tugas berhasil.");
        await progress.StepAsync($"{done}/{tasks.Count} tugas pemulihan selesai", cancellationToken, last: true);
    }
}
