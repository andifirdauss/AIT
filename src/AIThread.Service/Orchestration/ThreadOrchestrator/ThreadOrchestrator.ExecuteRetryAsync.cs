using System.Diagnostics;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Models;
using AIThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // chat is the chat turn that asked for the retry ("ulangi"): the answer then links the dashboard and the context
    // pack of the chat is updated, as after a normal turn.
    private async Task<ThreadResult> ExecuteRetryAsync(PreparedRetry prepared, IThreadObserver observer, ThreadRequest? chat = null)
    {
        var thread = prepared.Thread;
        var plan = prepared.Plan;
        var cancellationToken = prepared.Cancellation.Token;
        var recorder = new ThreadRecorder(dbFactory, eventBus, activityLog, thread);
        var total = Stopwatch.StartNew();
        using var secretTally = secrets.BeginTally();
        var request = new ThreadRequest(thread.Source, thread.Prompt, null, chat?.ConversationContext, thread.WorkingDirectory, prepared.Project.ProjectId,
            thread.SessionId, chat?.Session, DashboardUrl: chat?.DashboardUrl);
        var keepContext = chat is not null;

        RuntimeSettings settings;
        try
        {
            settings = await settingService.GetRuntimeAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            registry.Complete(thread.Id);
            return await FailAsync(recorder, new ClientProgress(observer, true), total, Lang.T($"Pengaturan tidak dapat dibaca: {ex.Message}", $"Settings cannot be read: {ex.Message}"));
        }

        var progress = new ClientProgress(observer, settings.StreamProgressToClient);
        return await GuardAsync(recorder, progress, total, request, keepContext, async () =>
        {
            var project = prepared.Project;
            await progress.AnchorAsync(ThreadAnchor.Line(project.Name, thread.Id), cancellationToken);
            var executors = await nodeService.GetActiveAsync(NodeRole.Executor, cancellationToken);
            if (executors.Count == 0) throw new OrchestrationException(Lang.T("Belum ada node Executor aktif. Tambahkan di menu Node.", "No active Executor node yet. Add one in the Node menu."));
            var wrapper = (await nodeService.GetActiveAsync(NodeRole.Wrapper, cancellationToken)).FirstOrDefault();
            var planner = (await nodeService.GetActiveAsync(NodeRole.Planner, cancellationToken)).FirstOrDefault();
            var memoryNode = wrapper ?? planner;
            // The stored plan has a test section only when it was made with Auto Testing on.
            var tester = plan.Test is null ? null : await AutoTesterAsync(planner, recorder, cancellationToken);

            var retryIds = prepared.RetryTasks.Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var planIds = plan.Tasks.Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            await recorder.UpdateThreadAsync(t =>
            {
                t.Status = ThreadStatus.Executing;
                t.RetryCount++;
                t.Error = null;
                t.FinalResponse = null;
                t.CompletedAt = null;
            });
            recorder.Log(ActivityLevel.Info,
                Lang.T($"Percobaan ulang ke-{thread.RetryCount}: {retryIds.Count} tugas dijalankan lagi dari rencana tersimpan ({string.Join(", ", retryIds)}); planner tidak dipanggil.", $"Retry #{thread.RetryCount}: {retryIds.Count} task(s) run again from the stored plan ({string.Join(", ", retryIds)}); the planner is not called."));
            await progress.PhaseAsync("🔁", Lang.T($"Percobaan ulang · {retryIds.Count} tugas", $"Retry · {retryIds.Count} task(s)"), cancellationToken);
            await progress.StepAsync(Lang.T($"mengulang yang belum berhasil: {string.Join(", ", retryIds)}", $"repeating what did not succeed: {string.Join(", ", retryIds)}"), cancellationToken, last: true);

            // Fix tasks of the earlier verification that did not succeed: the new verification replaces them.
            foreach (var stale in prepared.Rows.Where(r => !planIds.Contains(r.TaskKey) && r.Status != ThreadTaskStatus.Succeeded))
            {
                await recorder.UpdateTaskAsync(stale, e =>
                {
                    e.Status = ThreadTaskStatus.Skipped;
                    e.Error = Lang.T("Digantikan oleh percobaan ulang: verifikasi dijalankan lagi.", "Replaced by a retry: verification runs again.");
                });
            }

            // Reset the rows of the tasks to run again; a thread that stopped before its tasks were created gets them now.
            var rowsByKey = prepared.Rows.GroupBy(r => r.TaskKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
            var nextOrder = prepared.Rows.Count == 0 ? 1 : prepared.Rows.Max(r => r.Order) + 1;
            var missing = prepared.RetryTasks.Where(t => !rowsByKey.ContainsKey(t.Id)).ToList();
            if (missing.Count > 0)
            {
                foreach (var created in await recorder.CreateTasksAsync(new ThreadPlan { Tasks = missing }, nextOrder)) rowsByKey[created.TaskKey] = created;
                nextOrder += missing.Count;
            }

            var entities = new List<ThreadTask>();
            foreach (var task in prepared.RetryTasks)
            {
                var row = rowsByKey[task.Id];
                await recorder.UpdateTaskAsync(row, e =>
                {
                    e.Status = ThreadTaskStatus.Pending;
                    e.Summary = null;
                    e.Diff = null;
                    e.Error = null;
                    e.Tokens = 0;
                    e.Attempts = 0;
                    e.StartedAt = null;
                    e.CompletedAt = null;
                    e.DurationMs = null;
                });
                entities.Add(row);
            }

            // Dependencies on tasks that already succeeded are satisfied.
            var retryPlan = new ThreadPlan
            {
                Summary = plan.Summary,
                Contract = plan.Contract,
                Tasks = prepared.RetryTasks.Select(t => CopyTask(t, t.DependsOn?.Where(retryIds.Contains).ToList())).ToList()
            };

            var scope = new WorkspaceScope(project.RootPath, ProjectLocator.IgnoredDirectoriesFor(project.Stacks));
            var journal = new FileJournal(dbFactory, thread.Id, scope);
            await progress.PhaseAsync("🚀", Lang.T($"Eksekusi · {executors.Count} executor paralel", $"Execution · {executors.Count} parallel executors"), cancellationToken);
            var execution = await executionEngine.RunAsync(new ExecutionRequest(retryPlan, entities, executors, scope, settings, journal), recorder, progress, cancellationToken);
            await recorder.UpdateThreadAsync(t =>
            {
                t.ExecutionWallMs = (t.ExecutionWallMs ?? 0) + execution.WallMs;
                t.ExecutionSumMs = (t.ExecutionSumMs ?? 0) + execution.SumMs;
                t.ExecutorTokens += execution.Tokens;
            });
            await ReportSpeedAsync(progress, execution, cancellationToken);

            // Verification and the wrapper see the whole thread: the plan, fix tasks that succeeded before, and the new results.
            var kept = prepared.Rows.Where(r => r.Status == ThreadTaskStatus.Succeeded).ToList();
            var keptByKey = kept.GroupBy(r => r.TaskKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
            var tasks = plan.Tasks.Concat(keptByKey.Values.Where(r => !planIds.Contains(r.TaskKey)).OrderBy(r => r.Order).Select(ToPlanTask)).ToList();
            var fresh = execution.Results.ToDictionary(r => r.TaskKey, StringComparer.OrdinalIgnoreCase);
            var results = tasks.Select(t => fresh.TryGetValue(t.Id, out var result) ? result : ToResult(keptByKey[t.Id])).ToList();
            var combined = new ExecutionSummary(results, execution.WallMs, execution.SumMs, execution.Tokens);
            var fullPlan = new ThreadPlan { Summary = plan.Summary, Answer = plan.Answer, Contract = plan.Contract, Tasks = tasks, Test = plan.Test };

            var pipeline = new Pipeline(recorder, progress, request, project, executors, wrapper, memoryNode, scope, settings, journal, total, keepContext,
                Tester: tester);
            return await CompleteAsync(pipeline, fullPlan, combined, nextOrder, PreviousRounds(thread.VerificationJson) + 1, cancellationToken);
        }, cancellationToken);
    }
}
