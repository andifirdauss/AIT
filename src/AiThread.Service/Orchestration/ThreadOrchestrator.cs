using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Data.Context;
using AiThread.Data.Messaging;
using AiThread.Service.Business;
using AiThread.Service.CodeCalls;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Verification;
using AiThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Orchestration;

// planner (expensive, explores + plans) -> executors (cheap, parallel, one file each) -> verification (build and/or
// reviewer, with fix rounds) -> wrapper (pools and reports).
public sealed class ThreadOrchestrator(
    ISettingService settingService,
    INodeService nodeService,
    IProjectService projectService,
    IDbContextFactory<AiThreadDbContext> dbFactory,
    IEventBus eventBus,
    IActivityLogService activityLog,
    ThreadRegistry registry,
    PlannerAgent plannerAgent,
    ExecutionEngine executionEngine,
    VerificationStage verificationStage,
    WrapperAgent wrapperAgent,
    MemoryExtractor memoryExtractor,
    Memory.CodeMapper codeMapper,
    ContextKeeper contextKeeper,
    ICodeStructureService codeStructure,
    FunctionCallQueue functionCalls,
    PendingQuestionStore questions,
    INineRouterService router,
    IHostApplicationLifetime lifetime,
    ILogger<ThreadOrchestrator> logger) : IThreadOrchestrator
{
    // How long the final answer waits for the memory extraction before its footer says "still running".
    private static readonly TimeSpan MemoryWait = TimeSpan.FromSeconds(30);

    // Files of the session a follow-up's labels are taken from, at most.
    private const int MaxSessionLabelFiles = 30;

    public async Task<ThreadResult> RunAsync(ThreadRequest request, IThreadObserver observer, CancellationToken cancellationToken)
    {
        var prepared = await PrepareOrResumeAsync(request, cancellationToken);
        return await ExecuteAsync(prepared, observer, cancellationToken);
    }

    public async Task<Guid> StartInBackgroundAsync(ThreadRequest request, CancellationToken cancellationToken)
    {
        var prepared = await PrepareOrResumeAsync(request, cancellationToken);
        _ = Task.Run(() => ExecuteAsync(prepared, NullThreadObserver.Instance, lifetime.ApplicationStopping), CancellationToken.None);
        return prepared.Thread.Id;
    }

    // A prompt from a session whose thread is waiting on a planner question is the answer: that thread continues.
    private async Task<PreparedThread> PrepareOrResumeAsync(ThreadRequest request, CancellationToken cancellationToken)
    {
        var pending = questions.TryTake(request.SessionId, request.ProjectId, out var stale);
        if (stale is not null)
            await UnansweredQuestions.CloseAsync(dbFactory, eventBus, [stale.ThreadId], "Pertanyaan planner tidak dijawab; prompt berikutnya dijalankan sebagai thread baru.");

        if (pending is not null && await ResumeAsync(pending, request, cancellationToken) is { } resumed) return resumed;
        return await PrepareAsync(request, cancellationToken);
    }

    private async Task<PreparedThread?> ResumeAsync(PendingQuestion pending, ThreadRequest request, CancellationToken cancellationToken)
    {
        ThreadRun? thread;
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
            thread = await db.Threads.AsNoTracking().FirstOrDefaultAsync(t => t.Id == pending.ThreadId, cancellationToken);
        if (thread is not { Status: ThreadStatus.AwaitingInput }) return null;

        var project = await projectService.GetContextAsync(pending.ProjectId, cancellationToken);
        if (project is null) return null;

        // Wrapper, reviewer and context keeper see the original request together with the clarification.
        var clarified = request with
        {
            Prompt = $"{thread.Prompt}\n\n---\nAIT asked:{pending.Question.Question}\nDeveloper answered: {request.Prompt}"
        };
        activityLog.Info("thread", $"Jawaban untuk pertanyaan planner diterima: {Truncate(request.Prompt.ReplaceLineEndings(" "), 160)}", thread.Id, thread.ProjectId);
        return new PreparedThread(thread, project, clarified, pending, request.Prompt);
    }

    private async Task<PreparedThread> PrepareAsync(ThreadRequest request, CancellationToken cancellationToken)
    {
        var project = await projectService.GetContextAsync(request.ProjectId, cancellationToken)
                      ?? throw new AppValidationException("Project tidak ditemukan. Daftarkan ulang di menu Project.");

        var thread = new ThreadRun
        {
            ProjectId = project.ProjectId,
            Source = request.Source,
            SessionId = request.SessionId,
            WorkingDirectory = Truncate(request.WorkingDirectory ?? project.RootPath, 800),
            Prompt = request.Prompt,
            Status = ThreadStatus.Pending
        };

        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            db.Threads.Add(thread);
            // Kept until the business wiki writer has summarized the thread: a screenshot may belong on a page.
            db.ThreadAttachments.AddRange(BusinessImages.FromPrompt(thread.Id, request.Images));
            await db.SaveChangesAsync(cancellationToken);
        }

        eventBus.Publish(AppEvent.Create(AppEventTypes.ThreadCreated, new { thread.Id, thread.ProjectId, thread.Status }, thread.Id));
        activityLog.Info("thread",
            $"Thread baru dari {SourceLabel(request.Source)} · {project.Name}: {Truncate(request.Prompt.ReplaceLineEndings(" "), 160)}",
            thread.Id, thread.ProjectId);

        return new PreparedThread(thread, project, request);
    }

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
            var (codeMap, labelsFromSession) = await PromptCodeMapAsync(project, request, thread.Id, cancellationToken);
            var plannerInput = new PlannerInput(request, project, planners, scope, settings, canAsk, codeMap, labelsFromSession);
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
            await progress.StepAsync(planning.Pause is null ? $"selesai dalam {Seconds(planClock.ElapsedMilliseconds)}" : "menunggu jawaban developer",
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

    // Runs the unfinished tasks of a finished thread again from its stored plan (no planner call), then verifies and
    // wraps like a normal run. Tasks that succeeded are kept; earlier fix tasks that did not succeed are replaced by
    // the new verification.
    public async Task<ThreadResult> RetryAsync(Guid threadId, IThreadObserver observer, CancellationToken cancellationToken)
    {
        var prepared = await PrepareRetryAsync(threadId, cancellationToken, cancellationToken);
        return await ExecuteRetryAsync(prepared, observer);
    }

    public async Task<Guid> RetryInBackgroundAsync(Guid threadId, CancellationToken cancellationToken)
    {
        var prepared = await PrepareRetryAsync(threadId, lifetime.ApplicationStopping, cancellationToken);
        _ = Task.Run(() => ExecuteRetryAsync(prepared, NullThreadObserver.Instance), CancellationToken.None);
        return threadId;
    }

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

    private async Task<ThreadResult> ExecuteRetryAsync(PreparedRetry prepared, IThreadObserver observer)
    {
        var thread = prepared.Thread;
        var plan = prepared.Plan;
        var cancellationToken = prepared.Cancellation.Token;
        var recorder = new ThreadRecorder(dbFactory, eventBus, activityLog, thread);
        var total = Stopwatch.StartNew();
        var request = new ThreadRequest(thread.Source, thread.Prompt, null, null, thread.WorkingDirectory, prepared.Project.ProjectId, thread.SessionId);

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
        return await GuardAsync(recorder, progress, total, request, keepContext: false, async () =>
        {
            var project = prepared.Project;
            var executors = await nodeService.GetActiveAsync(NodeRole.Executor, cancellationToken);
            if (executors.Count == 0) throw new OrchestrationException("Belum ada node Executor aktif. Tambahkan di menu Node.");
            var wrapper = (await nodeService.GetActiveAsync(NodeRole.Wrapper, cancellationToken)).FirstOrDefault();
            var memoryNode = wrapper ?? (await nodeService.GetActiveAsync(NodeRole.Planner, cancellationToken)).FirstOrDefault();

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
                $"Percobaan ulang ke-{thread.RetryCount}: {retryIds.Count} tugas dijalankan lagi dari rencana tersimpan ({string.Join(", ", retryIds)}); planner tidak dipanggil.");
            await progress.PhaseAsync("🔁", $"Percobaan ulang · {retryIds.Count} tugas", cancellationToken);
            await progress.StepAsync($"mengulang yang belum berhasil: {string.Join(", ", retryIds)}", cancellationToken, last: true);

            // Fix tasks of the earlier verification that did not succeed: the new verification replaces them.
            foreach (var stale in prepared.Rows.Where(r => !planIds.Contains(r.TaskKey) && r.Status != ThreadTaskStatus.Succeeded))
            {
                await recorder.UpdateTaskAsync(stale, e =>
                {
                    e.Status = ThreadTaskStatus.Skipped;
                    e.Error = "Digantikan oleh percobaan ulang: verifikasi dijalankan lagi.";
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
            await progress.PhaseAsync("🚀", $"Eksekusi · {executors.Count} executor paralel", cancellationToken);
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
            var fullPlan = new ThreadPlan { Summary = plan.Summary, Answer = plan.Answer, Contract = plan.Contract, Tasks = tasks };

            var pipeline = new Pipeline(recorder, progress, request, project, executors, wrapper, memoryNode, scope, settings, journal, total, KeepContext: false);
            return await CompleteAsync(pipeline, fullPlan, combined, nextOrder, PreviousRounds(thread.VerificationJson) + 1, cancellationToken);
        }, cancellationToken);
    }

    // Verification (build and/or reviewer, with fix rounds), then the final answer, then the thread is finished.
    // plan and execution cover every task the developer should hear about. The answer is the status (FinalReport),
    // the wrapper's text, the changed files and, once the memory extraction is done, the footer; the thread stores
    // only the wrapper's text (or the automatic summary) as its final response.
    private async Task<ThreadResult> CompleteAsync(Pipeline pipeline, ThreadPlan plan, ExecutionSummary execution, int nextTaskOrder, int firstRound, CancellationToken cancellationToken)
    {
        var (recorder, progress, request, wrapper) = (pipeline.Recorder, pipeline.Progress, pipeline.Request, pipeline.Wrapper);
        var thread = recorder.Thread;

        // 3. Verify: build and/or review, with fix rounds. From here on plan and execution include the fix tasks.
        var verification = await VerifyAsync(pipeline, plan, execution, nextTaskOrder, firstRound, cancellationToken);
        if (verification is not null)
        {
            plan = verification.Plan;
            execution = verification.Execution;
        }

        // 4. Wrap
        await recorder.UpdateThreadAsync(t =>
        {
            t.Status = ThreadStatus.Wrapping;
            t.WrapperModel = wrapper?.Model;
        });

        if (wrapper is not null) await progress.PhaseAsync("📦", $"Rangkuman · {wrapper.Model}", cancellationToken);
        await progress.FinalAsync(FinalReport.Status(execution, verification?.Report), cancellationToken);

        // Without the wrapper's text the developer still gets the plan summary between the status and the files.
        var wrapClock = Stopwatch.StartNew();
        string finalText;
        var wrapTokens = 0;
        var narrated = false;
        if (wrapper is null)
        {
            finalText = WrapperAgent.FallbackSummary(plan, execution, verification?.Report);
            await progress.FinalAsync(plan.Summary, cancellationToken);
        }
        else
        {
            try
            {
                AiNode answered;
                (finalText, wrapTokens, answered) = await wrapperAgent.WrapAsync(wrapper, request.Prompt, plan, execution, verification?.ForModels,
                    delta =>
                    {
                        narrated = true;
                        return progress.FinalAsync(delta, cancellationToken);
                    }, cancellationToken,
                    change => change.ReportAsync("Wrapper", recorder, progress, cancellationToken));
                if (answered.Id != wrapper.Id) await recorder.UpdateThreadAsync(t => t.WrapperModel = answered.Model);
                if (string.IsNullOrWhiteSpace(finalText))
                {
                    finalText = WrapperAgent.FallbackSummary(plan, execution, verification?.Report);
                    if (!narrated) await progress.FinalAsync(plan.Summary, cancellationToken);
                }
            }
            catch (NineRouterException ex)
            {
                recorder.Log(ActivityLevel.Warning, $"Wrapper gagal ({ex.Message}); memakai ringkasan otomatis.");
                finalText = WrapperAgent.FallbackSummary(plan, execution, verification?.Report);
                if (!narrated) await progress.FinalAsync(plan.Summary, cancellationToken);
            }
        }
        wrapClock.Stop();
        await progress.FinalAsync(FinalReport.ChangedFiles(execution), cancellationToken);

        var succeeded = execution.Results.Count(r => r.Status == ThreadTaskStatus.Succeeded);
        var status = succeeded == 0 ? ThreadStatus.Failed : ThreadStatus.Completed;
        var notes = new List<string>();
        if (succeeded < execution.Results.Count) notes.Add($"{execution.Results.Count - succeeded} tugas tidak berhasil.");
        if (verification?.Report.Outcome == VerificationReport.Failed) notes.Add(VerificationNote(verification.Report));
        await FinishAsync(recorder, status, finalText, pipeline.Total, wrapTokens, wrapClock.ElapsedMilliseconds, notes.Count > 0 ? string.Join(" ", notes) : null);
        if (succeeded > 0) CodeChanged(pipeline.Project.ProjectId, thread.Id);

        recorder.Log(status == ThreadStatus.Completed ? ActivityLevel.Success : ActivityLevel.Error,
            $"Thread selesai: {succeeded}/{execution.Results.Count} tugas berhasil dalam {Seconds(pipeline.Total.ElapsedMilliseconds)}.");

        MemoryExtraction? memory = null;
        var memoryPending = false;
        if (succeeded > 0 && pipeline.MemoryNode is { } memoryNode)
            (memory, memoryPending) = await ExtractMemoryAsync(memoryNode, thread, plan, execution, finalText, cancellationToken);
        await FooterAsync(progress, request, pipeline.Settings, thread, status, pipeline.Total, pipeline.MemoriesUsed, memory, memoryPending, cancellationToken);

        if (pipeline.KeepContext) ScheduleContextKeeping(thread, request);
        return new ThreadResult(thread.Id, status, progress.Transcript, TotalTokens(thread));
    }

    // The thread's tasks are done (or stopped): Project > Detail → Struktur kode is walked again, and the functions the
    // thread changed get their calls read by the tools node. Both in the background.
    private void CodeChanged(Guid projectId, Guid threadId)
    {
        codeStructure.Refresh(projectId);
        functionCalls.ThreadChanged(projectId, threadId);
    }

    // Module and layer of the files the prompt names, for the planner's first memory search layer: stored entries and
    // sure rules now, the model in the background (the memory extraction waits for it). A follow-up that names no file
    // ("berlaku buat yang lainnya juga") continues the session instead: the files the session's last thread changed or
    // named, else the ones the recent conversation or the session summary names. Never fails the thread.
    private async Task<(IReadOnlyList<Memory.CodeMapHit> CodeMap, SessionLabelSource? FromSession)> PromptCodeMapAsync(
        ProjectContext project, ThreadRequest request, Guid currentThreadId, CancellationToken cancellationToken)
    {
        try
        {
            var files = await codeMapper.ResolveReferencesAsync(project, request.Prompt, cancellationToken);
            if (files.Count > 0) return (await codeMapper.MapQuicklyAsync(project, files, cancellationToken), null);

            var last = await LastSessionThreadAsync(request.Session, currentThreadId, cancellationToken);
            files = last?.Files ?? Array.Empty<string>();
            if (files.Count == 0 && last is not null) files = await codeMapper.ResolveReferencesAsync(project, last.Prompt, cancellationToken);
            if (files.Count == 0) files = await codeMapper.ResolveReferencesAsync(project, request.ConversationContext, cancellationToken);
            if (files.Count == 0) files = await codeMapper.ResolveReferencesAsync(project, request.Session?.Pack, cancellationToken);

            var hits = files.Count == 0 ? [] : await codeMapper.MapQuicklyAsync(project, files.Take(MaxSessionLabelFiles), cancellationToken);
            if (last is null && hits.Count == 0) return ([], null);
            return (hits, new SessionLabelSource(last?.Id, last?.Prompt));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Code map of the prompt's files failed.");
            return ([], null);
        }
    }

    // The latest earlier AIT thread of this chat (not the running one), with the files its succeeded tasks left behind.
    private async Task<SessionThread?> LastSessionThreadAsync(SessionContext? session, Guid currentThreadId, CancellationToken cancellationToken)
    {
        var threadId = session?.ThreadIds.LastOrDefault(id => id != currentThreadId) ?? Guid.Empty;
        if (threadId == Guid.Empty) return null;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var prompt = await db.Threads.AsNoTracking().Where(t => t.Id == threadId).Select(t => t.Prompt).FirstOrDefaultAsync(cancellationToken);
        if (prompt is null) return null;

        var tasks = await db.ThreadTasks.AsNoTracking()
            .Where(t => t.ThreadRunId == threadId && t.Status == ThreadTaskStatus.Succeeded)
            .OrderBy(t => t.Order)
            .Select(t => new { t.FilePath, t.TargetPath })
            .ToListAsync(cancellationToken);
        var files = tasks.Select(t => string.IsNullOrWhiteSpace(t.TargetPath) ? t.FilePath : t.TargetPath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new SessionThread(threadId, prompt, files);
    }

    private sealed record SessionThread(Guid Id, string Prompt, IReadOnlyList<string> Files);

    // The memory extraction runs on the application's token, so the memories are kept even when the client hangs up
    // while the answer waits. Pending: it did not finish within MemoryWait (or the client left); it goes on anyway.
    private async Task<(MemoryExtraction? Result, bool Pending)> ExtractMemoryAsync(
        AiNode node, ThreadRun thread, ThreadPlan plan, ExecutionSummary? execution, string finalText, CancellationToken cancellationToken)
    {
        var extraction = Task.Run(() => memoryExtractor.ExtractAsync(node, thread, plan, execution, finalText, lifetime.ApplicationStopping), CancellationToken.None);
        try
        {
            return (await extraction.WaitAsync(MemoryWait, cancellationToken), false);
        }
        catch (TimeoutException)
        {
            return (null, true);
        }
        catch (OperationCanceledException)
        {
            return (null, true);
        }
    }

    // Last part of the answer: time, tokens (memory extraction included), memory, wiki queue, the remaining quota of
    // the providers behind the nodes, and the thread page link.
    private async Task FooterAsync(
        ClientProgress progress,
        ThreadRequest request,
        RuntimeSettings settings,
        ThreadRun thread,
        ThreadStatus status,
        Stopwatch total,
        int memoriesUsed,
        MemoryExtraction? memory,
        bool memoryPending,
        CancellationToken cancellationToken)
    {
        // The thread is already finished; a client that left gets nothing more, and the thread stays as it is.
        if (cancellationToken.IsCancellationRequested) return;
        var wikiHours = settings.BusinessMemoryEnabled && status == ThreadStatus.Completed ? Math.Max(1, settings.BusinessMemoryIntervalHours) : (int?)null;
        var quotas = await NodeQuotasAsync(cancellationToken);
        var footer = FinalReport.Footer(total.Elapsed, TotalTokens(thread) + (memory?.Tokens ?? 0), memoriesUsed, memory, memoryPending, wikiHours,
            request.DashboardUrl, thread.Id, quotas);
        await progress.FinalAsync(footer, cancellationToken);
    }

    // Only the providers whose models the active nodes (and the fallbacks they hand calls to) use. Best effort: the
    // footer goes out without it when 9router's dashboard API does not answer.
    private async Task<IReadOnlyList<RouterQuotaDto>> NodeQuotasAsync(CancellationToken cancellationToken)
    {
        try
        {
            var nodes = await nodeService.GetAllAsync(cancellationToken);
            var byId = nodes.ToDictionary(n => n.Id);
            var used = new HashSet<Guid>();
            foreach (var node in nodes.Where(n => n.IsActive))
                for (NodeDto? current = node; current is not null && used.Add(current.Id);)
                    current = current.FallbackNodeId is { } next ? byId.GetValueOrDefault(next) : null;

            var models = nodes.Where(n => used.Contains(n.Id)).Select(n => n.Model.Trim()).Where(m => m.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return await router.GetQuotasAsync(models, cancellationToken);
        }
        // Also a client hanging up: the thread is already finished and must not turn into a cancelled one here.
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Provider quota for the footer unavailable");
            return [];
        }
    }

    // "📋 Rencana": the summary, then one entry per task (id, file and dependencies) with its title under it.
    private static async Task ReportPlanAsync(ClientProgress progress, ThreadPlan plan, CancellationToken cancellationToken)
    {
        await progress.PhaseAsync("📋", $"Rencana · {plan.Tasks.Count} tugas", cancellationToken);
        if (!string.IsNullOrWhiteSpace(plan.Summary)) await progress.TextAsync(plan.Summary, cancellationToken);
        for (var i = 0; i < plan.Tasks.Count; i++)
        {
            var task = plan.Tasks[i];
            var last = i == plan.Tasks.Count - 1;
            var dependencies = task.DependsOn is { Count: > 0 } deps ? $" ← {string.Join(", ", deps)}" : string.Empty;
            var indent = task.Id.Length + 1;
            await progress.StepAsync($"{task.Id} {task.Target}{dependencies}", cancellationToken, last, indent);
            await progress.DetailAsync(task.Title, cancellationToken, indent, underLast: last);
        }
    }

    // Turns cancellation and failures inside body into a finished thread; the registry entry is always released.
    private async Task<ThreadResult> GuardAsync(
        ThreadRecorder recorder,
        ClientProgress progress,
        Stopwatch total,
        ThreadRequest request,
        bool keepContext,
        Func<Task<ThreadResult>> body,
        CancellationToken cancellationToken)
    {
        var thread = recorder.Thread;
        try
        {
            return await body();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Tasks that finished before the cancel may have written files.
            CodeChanged(request.ProjectId, thread.Id);
            await progress.CloseAsync();
            await MarkUnfinishedTasksCancelledAsync(thread.Id);
            await FinishAsync(recorder, ThreadStatus.Cancelled, null, total, 0, null, "Thread dibatalkan.");
            recorder.Log(ActivityLevel.Warning, "Thread dibatalkan.");
            return new ThreadResult(thread.Id, ThreadStatus.Cancelled, progress.Transcript, TotalTokens(thread));
        }
        catch (Exception ex)
        {
            if (ex is not (OrchestrationException or NineRouterException)) logger.LogError(ex, "Thread {ThreadId} crashed", thread.Id);
            var message = ex is OrchestrationException or NineRouterException ? ex.Message : $"Kesalahan internal: {ex.Message}";
            CodeChanged(request.ProjectId, thread.Id);
            await MarkUnfinishedTasksCancelledAsync(thread.Id);
            var failed = await FailAsync(recorder, progress, total, message);
            if (keepContext) ScheduleContextKeeping(thread, request);
            return failed;
        }
        finally
        {
            registry.Complete(thread.Id);
        }
    }

    // Closes the execution phase: "7/8 berhasil · 24.8s (1.8x lebih cepat dari sekuensial)"; the speed-up only when
    // running in parallel actually saved time.
    private static async Task ReportSpeedAsync(ClientProgress progress, ExecutionSummary execution, CancellationToken cancellationToken)
    {
        var succeeded = execution.Results.Count(r => r.Status == ThreadTaskStatus.Succeeded);
        var speedup = execution.WallMs > 0 ? (double)execution.SumMs / execution.WallMs : 1;
        var gain = speedup >= 1.1 ? $" ({speedup.ToString("0.0", CultureInfo.InvariantCulture)}x lebih cepat dari sekuensial)" : string.Empty;
        await progress.StepAsync($"{succeeded}/{execution.Results.Count} berhasil · {Seconds(execution.WallMs)}{gain}", cancellationToken, last: true);
    }

    // Verification rounds already stored on the thread; a retry numbers its rounds after them.
    private static int PreviousRounds(string? verificationJson)
    {
        if (string.IsNullOrWhiteSpace(verificationJson)) return 0;
        try
        {
            return JsonSerializer.Deserialize<VerificationReport>(verificationJson, ThreadPlan.JsonOptions)?.Rounds.Count ?? 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static PlanTask CopyTask(PlanTask task, List<string>? dependsOn) => new()
    {
        Id = task.Id,
        Title = task.Title,
        File = task.File,
        Action = task.Action,
        To = task.To,
        Members = task.Members,
        Instruction = task.Instruction,
        Focus = task.Focus,
        Context = task.Context,
        DependsOn = dependsOn is { Count: > 0 } ? dependsOn : null
    };

    private static PlanTask ToPlanTask(ThreadTask row) => new()
    {
        Id = row.TaskKey,
        Title = row.Title,
        File = row.FilePath,
        Action = row.Action,
        To = row.TargetPath,
        Instruction = row.Instruction
    };

    private static TaskResult ToResult(ThreadTask row) =>
        new(row.TaskKey, row.FilePath, row.Status, row.Summary, row.Diff, row.Error, row.DurationMs ?? 0, row.Tokens);

    // The planner asked the developer something: the question is the reply, and the thread waits for the next prompt of
    // the session. The database is updated before the question is registered, so an instant answer finds it waiting.
    private async Task<ThreadResult> AwaitAnswerAsync(
        ThreadRecorder recorder,
        ClientProgress progress,
        ProjectContext project,
        ThreadRequest request,
        PlanningResult planning,
        PlannerPause pause,
        Stopwatch planClock,
        Stopwatch total,
        CancellationToken cancellationToken)
    {
        var thread = recorder.Thread;
        var text = pause.Question.ToText();
        await progress.FinalAsync(text, cancellationToken);
        await recorder.UpdateThreadAsync(t =>
        {
            t.Status = ThreadStatus.AwaitingInput;
            t.PlannerModel = planning.Node.Model;
            t.PlannerSteps = planning.Steps;
            t.PlannerTokens = planning.Tokens;
            t.PlannerCachedTokens = planning.CachedTokens;
            t.ToolsNodeTokens = planning.ToolsNodeTokens;
            t.PlanningMs = (t.PlanningMs ?? 0) + planClock.ElapsedMilliseconds;
            t.FinalResponse = text;
            t.TotalMs = (t.TotalMs ?? 0) + total.ElapsedMilliseconds;
        });
        questions.Add(thread.Id, project.ProjectId, request.SessionId!, pause.Question, pause.Conversation);
        recorder.Log(ActivityLevel.Info, $"Planner bertanya ke developer; thread menunggu jawaban: {Truncate(pause.Question.Question.ReplaceLineEndings(" "), 200)}");
        return new ThreadResult(thread.Id, ThreadStatus.AwaitingInput, progress.Transcript, TotalTokens(thread));
    }

    // Runs when something was changed and there is a way to check it: a build command (and building is allowed) or an
    // active Reviewer node. Null when skipped. A crash in here only costs the verification, not the edits already made.
    private async Task<VerificationResult?> VerifyAsync(
        Pipeline pipeline,
        ThreadPlan plan,
        ExecutionSummary execution,
        int nextTaskOrder,
        int firstRound,
        CancellationToken cancellationToken)
    {
        if (!execution.Results.Any(r => r.Status == ThreadTaskStatus.Succeeded)) return null;

        var (recorder, progress, project, settings) = (pipeline.Recorder, pipeline.Progress, pipeline.Project, pipeline.Settings);
        var reviewer = (await nodeService.GetActiveAsync(NodeRole.Reviewer, cancellationToken)).FirstOrDefault();
        var build = settings.BuildEnabled ? BuildCommandResolver.Resolve(project.RootPath, project.Modules, null, out _) : null;
        if (build is null && reviewer is null) return null;

        await recorder.UpdateThreadAsync(t =>
        {
            t.Status = ThreadStatus.Verifying;
            t.ReviewerModel = reviewer?.Model;
        });
        var what = string.Join(" + ", new[] { build?.Display, reviewer is null ? null : $"reviewer {reviewer.Model}" }.OfType<string>());
        await progress.PhaseAsync("🔍", $"Verifikasi · {what}", cancellationToken);

        var clock = Stopwatch.StartNew();
        try
        {
            var result = await verificationStage.RunAsync(
                new VerificationInput(pipeline.Request, plan, execution, build, reviewer, pipeline.Executors, pipeline.Scope, settings, nextTaskOrder,
                    pipeline.Journal, firstRound),
                recorder, progress, cancellationToken);
            clock.Stop();

            await recorder.UpdateThreadAsync(t =>
            {
                t.VerificationMs = clock.ElapsedMilliseconds;
                t.VerificationJson = result.Report.ToJson();
                t.ReviewerTokens += result.ReviewerTokens;
                t.ExecutorTokens += result.ExecutorTokens;
            });
            await progress.StepAsync(VerificationClosing(result.Report), cancellationToken, last: true);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Verification of thread {ThreadId} crashed", recorder.Thread.Id);
            recorder.Log(ActivityLevel.Error, $"Verifikasi gagal: {ex.Message}");
            await progress.StepAsync($"🟡 Verifikasi gagal: {ex.Message}", cancellationToken, hang: 3);
            await progress.StepAsync("verifikasi dihentikan", cancellationToken, last: true);
            await recorder.UpdateThreadAsync(t => t.VerificationMs = clock.ElapsedMilliseconds);
            return null;
        }
    }

    // Closes the verification phase: "lolos setelah 1 putaran perbaikan".
    private static string VerificationClosing(VerificationReport report)
    {
        var fixRounds = report.Rounds.Count(r => r.FixTasks.Count > 0);
        return report.Outcome switch
        {
            VerificationReport.Passed => fixRounds == 0 ? "lolos" : $"lolos setelah {fixRounds} putaran perbaikan",
            VerificationReport.Failed => "belum lolos, masih ada masalah (lihat ringkasan)",
            _ => "hasil tidak dapat dipastikan"
        };
    }

    private static string VerificationNote(VerificationReport report)
    {
        var last = report.Rounds.LastOrDefault();
        var parts = new List<string>();
        if (last?.Build is { Status: "failed" } build) parts.Add($"build masih gagal ({build.ErrorCount} error)");
        if (last?.Review is { Error: null, Issues.Count: > 0 } review) parts.Add($"reviewer masih menemukan {review.Issues.Count} masalah");
        return parts.Count == 0 ? "Verifikasi belum lolos." : $"Verifikasi: {string.Join(", ", parts)}.";
    }

    private async Task<ThreadResult> FailAsync(ThreadRecorder recorder, ClientProgress progress, Stopwatch total, string message)
    {
        await progress.FinalAsync($"❌ AIT gagal: {message}", CancellationToken.None);
        await FinishAsync(recorder, ThreadStatus.Failed, null, total, 0, null, message);
        recorder.Log(ActivityLevel.Error, $"Thread gagal: {message}");
        return new ThreadResult(recorder.Thread.Id, ThreadStatus.Failed, progress.Transcript, TotalTokens(recorder.Thread));
    }

    private static Task FinishAsync(ThreadRecorder recorder, ThreadStatus status, string? finalText, Stopwatch total, int wrapTokens, long? wrappingMs, string? error = null) =>
        recorder.UpdateThreadAsync(t =>
        {
            t.Status = status;
            t.FinalResponse = finalText;
            t.WrapperTokens += wrapTokens;
            t.WrappingMs = wrappingMs;
            t.Error = error;
            // A thread resumed after a planner question already holds the time spent before it.
            t.TotalMs = (t.TotalMs ?? 0) + total.ElapsedMilliseconds;
            t.CompletedAt = DateTime.UtcNow;
        });

    private async Task MarkUnfinishedTasksCancelledAsync(Guid threadId)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            await db.ThreadTasks
                .Where(t => t.ThreadRunId == threadId && (t.Status == ThreadTaskStatus.Pending || t.Status == ThreadTaskStatus.Running))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Status, ThreadTaskStatus.Cancelled)
                    .SetProperty(t => t.CompletedAt, DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not mark tasks of thread {ThreadId} as cancelled", threadId);
        }
    }

    // Only Claude Code chats carry thread anchors, so only their next turn can pick the pack up.
    private void ScheduleContextKeeping(ThreadRun thread, ThreadRequest request)
    {
        if (request.Source != ThreadSource.ClaudeCode) return;
        _ = Task.Run(() => contextKeeper.UpdateAsync(thread, request, lifetime.ApplicationStopping), CancellationToken.None);
    }

    private static int TotalTokens(ThreadRun thread) => thread.PlannerTokens + thread.ToolsNodeTokens + thread.ExecutorTokens + thread.ReviewerTokens + thread.WrapperTokens;

    private static string Seconds(long milliseconds) => $"{milliseconds / 1000.0:0.0}s";

    private static string SourceLabel(ThreadSource source) => source switch
    {
        ThreadSource.ClaudeCode => "Claude Code",
        ThreadSource.Telegram => "Telegram",
        _ => "dashboard"
    };

    private static string Truncate(string? value, int max) =>
        value is null ? string.Empty : value.Length <= max ? value : value[..max];

    // Resume and Reply are set when the thread continues after a planner question; Request then carries the
    // original request plus the clarification.
    private sealed record PreparedThread(ThreadRun Thread, ProjectContext Project, ThreadRequest Request, PendingQuestion? Resume = null, string? Reply = null);

    // Rows are all task rows of the thread; RetryTasks the planned tasks that did not succeed. Cancellation is the
    // registry entry taken while preparing.
    private sealed record PreparedRetry(
        ThreadRun Thread,
        ProjectContext Project,
        ThreadPlan Plan,
        IReadOnlyList<ThreadTask> Rows,
        IReadOnlyList<PlanTask> RetryTasks,
        CancellationTokenSource Cancellation);

    // What verification and wrapping of a run need. MemoryNode extracts memories afterwards (null = skip);
    // KeepContext updates the Claude Code context pack (not for retries, which no chat turn started); MemoriesUsed is
    // how many memories the planner read, for the footer.
    private sealed record Pipeline(
        ThreadRecorder Recorder,
        ClientProgress Progress,
        ThreadRequest Request,
        ProjectContext Project,
        IReadOnlyList<AiNode> Executors,
        AiNode? Wrapper,
        AiNode? MemoryNode,
        WorkspaceScope Scope,
        RuntimeSettings Settings,
        FileJournal Journal,
        Stopwatch Total,
        bool KeepContext,
        int MemoriesUsed = 0);
}
