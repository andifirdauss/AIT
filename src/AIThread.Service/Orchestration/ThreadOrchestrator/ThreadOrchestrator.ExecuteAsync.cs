using System.Diagnostics;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Approvals;
using AIThread.Service.Models;
using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

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
        // The secrets masked on the way to the models of this thread, for the footer.
        using var secretTally = secrets.BeginTally();

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
        return await GuardAsync(recorder, progress, total, request, keepContext: true, async () =>
        {
            var project = prepared.Project;
            await progress.AnchorAsync(ThreadAnchor.Line(project.Name, thread.Id), cancellationToken);

            // The first active planner plans; its fallback chain takes over when it is rate limited or down.
            var planners = await nodeService.GetActiveAsync(NodeRole.Planner, cancellationToken);
            var planner = planners.FirstOrDefault()
                          ?? throw new OrchestrationException(Lang.T("Belum ada node Planner aktif. Tambahkan di menu Node.", "No active Planner node yet. Add one in the Node menu."));
            var executors = await nodeService.GetActiveAsync(NodeRole.Executor, cancellationToken);
            var wrapper = (await nodeService.GetActiveAsync(NodeRole.Wrapper, cancellationToken)).FirstOrDefault();
            var tester = await AutoTesterAsync(planner, recorder, cancellationToken);
            var scope = new WorkspaceScope(project.RootPath, ProjectLocator.IgnoredDirectoriesFor(project.Stacks));

            // 0. The project's unit tests: missing ones come back, and the related ones run while the planner plans.
            var unitTests = await unitTestStage.OpenAsync(project, settings, cancellationToken);
            var (codeMap, labelsFromSession, promptFiles, previousTurn) = await PromptCodeMapAsync(project, request, thread.Id, cancellationToken);
            var sessionChanges = await SessionChangesAsync(project, request.Session, thread.Id, cancellationToken);
            // Without a test project there is nothing to restore into or to run before the change.
            if (unitTests is { Suites.Count: > 0 })
            {
                await RestoreUnitTestsAsync(unitTests, executors, scope, settings, recorder, progress, cancellationToken);
                unitTestStage.StartBaseline(unitTests, promptFiles.Concat(codeMap.Select(h => h.Path)).ToList(), codeMap.Select(h => h.Module).Distinct().ToList(),
                    recorder, cancellationToken);
            }

            // 1. Plan
            await recorder.UpdateThreadAsync(t =>
            {
                t.Status = ThreadStatus.Planning;
                t.PlannerModel = planner.Model;
            });
            // A question needs a way back: a session to answer in, and not a dashboard run nobody is watching.
            var canAsk = settings.PlannerCanAsk && request.Source != ThreadSource.Dashboard && !string.IsNullOrEmpty(request.SessionId);
            // Permission can always be asked of a session, and of a dashboard thread through its page.
            var canApprove = !string.IsNullOrEmpty(request.SessionId) || request.Source == ThreadSource.Dashboard;
            var plannerInput = new PlannerInput(request, project, planners, scope, settings, canAsk, codeMap, labelsFromSession, promptFiles,
                AutoTesting: tester is not null, PreviousTurn: previousTurn, UnitTests: unitTests, SessionChanges: sessionChanges, CanApprove: canApprove);
            var planClock = Stopwatch.StartNew();
            PlanningResult planning;
            await progress.PhaseAsync("🧠", $"Planner · {planner.Model}", cancellationToken);
            if (prepared.Resume is { Question.Approval: { } approval } waiting)
            {
                var decision = ApprovalReply.Parse(prepared.Reply);
                await progress.StepAsync(decision is ApprovalDecision.Once or ApprovalDecision.Always or ApprovalDecision.AlwaysPattern or ApprovalDecision.AlwaysAll
                    ? Lang.T("🔐 Izin diberikan, rencana dilanjutkan", "🔐 Permission given, the plan goes on")
                    : Lang.T("🔐 Izin ditolak, planner mencari jalan lain", "🔐 Permission denied, the planner looks for another way"), cancellationToken, hang: 3);
                planning = await plannerAgent.ResumeApprovalAsync(plannerInput, waiting.Conversation, approval, decision, prepared.Reply ?? string.Empty, recorder, progress, cancellationToken);
            }
            else if (prepared.Resume is { } resume)
            {
                await progress.StepAsync(Lang.T("💬 Jawaban diterima, rencana dilanjutkan", "💬 Answer received, the plan goes on"), cancellationToken, hang: 3);
                planning = await plannerAgent.ResumeAsync(plannerInput, resume.Conversation, resume.Question.AnswerFor(prepared.Reply ?? string.Empty), recorder, progress, cancellationToken);
            }
            else
            {
                planning = await plannerAgent.PlanAsync(plannerInput, recorder, progress, cancellationToken);
            }
            planClock.Stop();
            planner = planning.Node;
            await progress.StepAsync(planning.Pause is null ? Lang.T($"selesai dalam {Seconds(planClock.ElapsedMilliseconds)} ({PlanningTimes(planning)}) · {planning.Steps} giliran · {FinalReport.Tokens(planning.Tokens)} token", $"done in {Seconds(planClock.ElapsedMilliseconds)} ({PlanningTimes(planning)}) · {planning.Steps} turns · {FinalReport.Tokens(planning.Tokens)} tokens") + (planning.CachedTokens > 0 ? $" · c{FinalReport.Tokens(planning.CachedTokens)}" : string.Empty) + (planning.ToolsNodeTokens > 0 ? $" · sub-agent {FinalReport.Tokens(planning.ToolsNodeTokens)}" : string.Empty) : Lang.T("menunggu jawaban developer", "waiting for the developer's answer"),
                cancellationToken, last: true);

            if (planning.Pause is { } pause)
                return await AwaitAnswerAsync(recorder, progress, project, request, planning, pause, planClock, total, cancellationToken);

            var plan = planning.Plan!;
            // The planner's dev servers served the investigation; the change and its testing need them gone (stale code, busy ports).
            StopBackgroundProcesses(recorder);
            // The developer allowed the plan's folders outside the project root (once or always).
            scope.AllowWrites(plan.OutsideFolders ?? []);
            // A project without a test project keeps the session only for the test project and tests the plan adds.
            if (unitTests is { Suites.Count: 0 } && plan.Tests?.New is not { Count: > 0 }) unitTests = null;
            await recorder.UpdateThreadAsync(t =>
            {
                t.PlannerModel = planner.Model;
                t.PlanJson = plan.ToJson();
                t.PlanSummary = Truncate(plan.Summary, 2000);
                t.PlannerSteps = planning.Steps;
                t.PlannerTokens = planning.Tokens;
                t.PlannerCachedTokens = planning.CachedTokens;
                t.ToolsNodeTokens = planning.ToolsNodeTokens;
                t.InvestigationJson = recorder.Investigation.ToJson();
                t.PlanningMs = (t.PlanningMs ?? 0) + planClock.ElapsedMilliseconds;
            });

            if (plan.Tasks.Count == 0)
            {
                // The developer only wants the existing behaviour tested: unit tests and/or the running app.
                var testOnly = (unitTests is not null && plan.Tests is { Run.Count: > 0 }) || (tester is not null && plan.Test is { IsRunnable: true });
                if (testOnly)
                {
                    var onlyPipeline = new Pipeline(recorder, progress, request, project, executors, wrapper, wrapper ?? planner, scope, settings,
                        new FileJournal(dbFactory, thread.Id, scope), total, KeepContext: true, planning.MemoriesUsed, tester, unitTests);
                    return await TestOnlyAsync(onlyPipeline, plan, cancellationToken);
                }

                // The baseline may still run; its results reach the catalog when it ends, without holding up the answer.
                if (unitTests is not null) _ = Task.Run(() => unitTestStage.FinishAsync(unitTests, plan, null, thread.Id, recorder, CancellationToken.None), CancellationToken.None);
                var answer = string.IsNullOrWhiteSpace(plan.Answer) ? plan.Summary : plan.Answer;
                await progress.FinalAsync(answer, cancellationToken);
                recorder.Log(ActivityLevel.Success, Lang.T("Planner menjawab langsung tanpa tugas eksekusi.", "The planner answered directly without execution tasks."));
                await FinishAsync(recorder, ThreadStatus.Completed, answer, total, 0, null);
                var (memory, memoryPending) = await ExtractMemoryAsync(wrapper ?? planner, thread, plan, null, answer, cancellationToken);
                await FooterAsync(progress, request, settings, thread, ThreadStatus.Completed, total, planning.MemoriesUsed, memory, memoryPending, cancellationToken);
                // "commit and push this change": the plan has no tasks, only a publish section.
                await PublishAsync(request, project, thread, plan, settings, null, progress, cancellationToken);
                ScheduleContextKeeping(thread, request);
                return new ThreadResult(thread.Id, ThreadStatus.Completed, progress.Transcript, TotalTokens(thread));
            }

            if (executors.Count == 0) throw new OrchestrationException(Lang.T("Belum ada node Executor aktif. Tambahkan di menu Node.", "No active Executor node yet. Add one in the Node menu."));

            await ReportPlanAsync(progress, plan, cancellationToken);
            recorder.Log(ActivityLevel.Info, Lang.T($"Rencana: {plan.Tasks.Count} tugas, {plan.Contract.Count} item kontrak. {plan.Summary}", $"Plan: {plan.Tasks.Count} tasks, {plan.Contract.Count} contract items. {plan.Summary}"));

            // The baseline covers every test the plan concerns before a file changes.
            if (unitTests is not null)
            {
                var planFiles = plan.Tasks.SelectMany(t => t.To is null ? [t.File] : new[] { t.File, t.To }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var planModules = (await codeMapper.MapQuicklyAsync(project, planFiles, cancellationToken)).Select(h => h.Module)
                    .Concat(codeMap.Select(h => h.Module)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                await unitTestStage.CompleteBaselineAsync(unitTests, plan, planModules, recorder, progress, cancellationToken);
            }

            // 2. Execute in parallel; every write is journaled so the thread can be undone.
            await recorder.UpdateThreadAsync(t => t.Status = ThreadStatus.Executing);
            await progress.PhaseAsync("🚀", Lang.T($"Eksekusi · {executors.Count} executor paralel", $"Execution · {executors.Count} parallel executors"), cancellationToken);
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

            // 3. Verify (build, review, unit tests), 4. test, 5. wrap.
            var triage = unitTests is null ? null : (await nodeService.GetActiveAsync(NodeRole.Tester, cancellationToken)).FirstOrDefault();
            var pipeline = new Pipeline(recorder, progress, request, project, executors, wrapper, wrapper ?? planner, scope, settings, journal, total, KeepContext: true,
                planning.MemoriesUsed, tester, unitTests, triage);
            return await CompleteAsync(pipeline, plan, execution, plan.Tasks.Count + 1, 1, cancellationToken);
        }, cancellationToken);
    }
}
