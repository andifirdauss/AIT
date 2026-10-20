using System.Diagnostics;
using AiThread.Common.Enums;
using AiThread.Service.Models;
using AiThread.Service.Workspace;

namespace AiThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    private async Task<ThreadResult> ExecuteAsync(PreparedThread prepared, IThreadObserver observer, CancellationToken externalToken)
    {
        var thread = prepared.Thread;
        var request = prepared.Request;
        using var cancellation = registry.Register(thread.Id, externalToken);
        var cancellationToken = cancellation.Token;
        var recorder = new ThreadRecorder(dbFactory, eventBus, activityLog, thread);
        var total = Stopwatch.StartNew();

        RuntimeSettings settings;
        try
        {
            settings = await settingService.GetRuntimeAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            registry.Complete(thread.Id);
            return await FailAsync(recorder, new ClientProgress(observer, true), total, $"Setting tidak dapat dibaca: {ex.Message}");
        }

        var progress = new ClientProgress(observer, settings.StreamProgressToClient);
        return await GuardAsync(recorder, progress, total, request, keepContext: true, async () =>
        {
            var project = prepared.Project;
            await progress.AnchorAsync(ThreadAnchor.Line(project.Name, thread.Id), cancellationToken);

            // The first active planner plans; its fallback chain takes over when it is rate limited or down.
            var planners = await nodeService.GetActiveAsync(NodeRole.Planner, cancellationToken);
            var planner = planners.FirstOrDefault()
                          ?? throw new OrchestrationException("Belum ada node Planner aktif. Tambahkan di menu Node.");
            var executors = await nodeService.GetActiveAsync(NodeRole.Executor, cancellationToken);
            var wrapper = (await nodeService.GetActiveAsync(NodeRole.Wrapper, cancellationToken)).FirstOrDefault();

            // 1. Plan
            await recorder.UpdateThreadAsync(t =>
            {
                t.Status = ThreadStatus.Planning;
                t.PlannerModel = planner.Model;
            });
            var scope = new WorkspaceScope(project.RootPath, ProjectLocator.IgnoredDirectoriesFor(project.Stacks));
            // A question needs a way back: a session to answer in, and not a dashboard run nobody is watching.
            var canAsk = settings.PlannerCanAsk && request.Source != ThreadSource.Dashboard && !string.IsNullOrEmpty(request.SessionId);
            var (codeMap, labelsFromSession, promptFiles) = await PromptCodeMapAsync(project, request, thread.Id, cancellationToken);
            var plannerInput = new PlannerInput(request, project, planners, scope, settings, canAsk, codeMap, labelsFromSession, promptFiles);
            var planClock = Stopwatch.StartNew();
            PlanningResult planning;
            await progress.PhaseAsync("🧠", $"Planner · {planner.Model}", cancellationToken);
            if (prepared.Resume is { } resume)
            {
                await progress.StepAsync("💬 Jawaban diterima, rencana dilanjutkan", cancellationToken, hang: 3);
                planning = await plannerAgent.ResumeAsync(plannerInput, resume.Conversation, resume.Question.AnswerFor(prepared.Reply ?? string.Empty), recorder, progress, cancellationToken);
            }
            else
            {
                planning = await plannerAgent.PlanAsync(plannerInput, recorder, progress, cancellationToken);
            }
            planClock.Stop();
            planner = planning.Node;
            await progress.StepAsync(planning.Pause is null ? $"selesai dalam {Seconds(planClock.ElapsedMilliseconds)} · {planning.Steps} giliran · {FinalReport.Tokens(planning.Tokens)} token" + (planning.CachedTokens > 0 ? $" · c{FinalReport.Tokens(planning.CachedTokens)}" : string.Empty) + (planning.ToolsNodeTokens > 0 ? $" · sub-agent {FinalReport.Tokens(planning.ToolsNodeTokens)}" : string.Empty) : "menunggu jawaban developer",
                cancellationToken, last: true);

            if (planning.Pause is { } pause)
                return await AwaitAnswerAsync(recorder, progress, project, request, planning, pause, planClock, total, cancellationToken);

            var plan = planning.Plan!;
            await recorder.UpdateThreadAsync(t =>
            {
                t.PlannerModel = planner.Model;
                t.PlanJson = plan.ToJson();
                t.PlanSummary = Truncate(plan.Summary, 2000);
                t.PlannerSteps = planning.Steps;
                t.PlannerTokens = planning.Tokens;
                t.PlannerCachedTokens = planning.CachedTokens;
                t.ToolsNodeTokens = planning.ToolsNodeTokens;
                t.PlanningMs = (t.PlanningMs ?? 0) + planClock.ElapsedMilliseconds;
            });

            if (plan.Tasks.Count == 0)
            {
                var answer = string.IsNullOrWhiteSpace(plan.Answer) ? plan.Summary : plan.Answer;
                await progress.FinalAsync(answer, cancellationToken);
                recorder.Log(ActivityLevel.Success, "Planner menjawab langsung tanpa tugas eksekusi.");
                await FinishAsync(recorder, ThreadStatus.Completed, answer, total, 0, null);
                var (memory, memoryPending) = await ExtractMemoryAsync(wrapper ?? planner, thread, plan, null, answer, cancellationToken);
                await FooterAsync(progress, request, settings, thread, ThreadStatus.Completed, total, planning.MemoriesUsed, memory, memoryPending, cancellationToken);
                ScheduleContextKeeping(thread, request);
                return new ThreadResult(thread.Id, ThreadStatus.Completed, progress.Transcript, TotalTokens(thread));
            }

            if (executors.Count == 0) throw new OrchestrationException("Belum ada node Executor aktif. Tambahkan di menu Node.");

            await ReportPlanAsync(progress, plan, cancellationToken);
            recorder.Log(ActivityLevel.Info, $"Rencana: {plan.Tasks.Count} tugas, {plan.Contract.Count} item kontrak. {plan.Summary}");

            // 2. Execute in parallel; every write is journaled so the thread can be undone.
            await recorder.UpdateThreadAsync(t => t.Status = ThreadStatus.Executing);
            await progress.PhaseAsync("🚀", $"Eksekusi · {executors.Count} executor paralel", cancellationToken);
            var taskEntities = await recorder.CreateTasksAsync(plan);
            var journal = new FileJournal(dbFactory, thread.Id, scope);
            var execution = await executionEngine.RunAsync(new ExecutionRequest(plan, taskEntities, executors, scope, settings, journal), recorder, progress, cancellationToken);

            await recorder.UpdateThreadAsync(t =>
            {
                t.ExecutionWallMs = execution.WallMs;
                t.ExecutionSumMs = execution.SumMs;
                t.ExecutorTokens = execution.Tokens;
            });
            await ReportSpeedAsync(progress, execution, cancellationToken);

            // 3. Verify, 4. wrap.
            var pipeline = new Pipeline(recorder, progress, request, project, executors, wrapper, wrapper ?? planner, scope, settings, journal, total, KeepContext: true,
                planning.MemoriesUsed);
            return await CompleteAsync(pipeline, plan, execution, plan.Tasks.Count + 1, 1, cancellationToken);
        }, cancellationToken);
    }
}
