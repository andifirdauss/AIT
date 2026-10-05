using System.Diagnostics;
using System.Text.Json;
using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Data.Context;
using AiThread.Data.Messaging;
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
    ContextKeeper contextKeeper,
    PendingQuestionStore questions,
    IHostApplicationLifetime lifetime,
    ILogger<ThreadOrchestrator> logger) : IThreadOrchestrator
{
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
            Prompt = $"{thread.Prompt}\n\n---\nAiThread asked: {pending.Question.Question}\nDeveloper answered: {request.Prompt}"
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
            var plannerInput = new PlannerInput(request, project, planners, scope, settings, canAsk);
            var planClock = Stopwatch.StartNew();
            PlanningResult planning;
            if (prepared.Resume is { } resume)
            {
                await progress.LineAsync($"💬 Jawaban diterima — planner `{planner.Model}` melanjutkan rencana…", cancellationToken);
                planning = await plannerAgent.ResumeAsync(plannerInput, resume.Conversation, resume.Question.AnswerFor(prepared.Reply ?? string.Empty), recorder, progress, cancellationToken);
            }
            else
            {
                await progress.LineAsync($"🧠 Planner `{planner.Model}` menganalisis project & menyusun rencana…", cancellationToken);
                planning = await plannerAgent.PlanAsync(plannerInput, recorder, progress, cancellationToken);
            }
            planClock.Stop();
            planner = planning.Node;

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
                t.PlanningMs = (t.PlanningMs ?? 0) + planClock.ElapsedMilliseconds;
            });

            if (plan.Tasks.Count == 0)
            {
                var answer = string.IsNullOrWhiteSpace(plan.Answer) ? plan.Summary : plan.Answer;
                await progress.FinalAsync(answer, cancellationToken);
                recorder.Log(ActivityLevel.Success, "Planner menjawab langsung tanpa tugas eksekusi.");
                await FinishAsync(recorder, ThreadStatus.Completed, answer, total, 0, null);
                ScheduleMemoryExtraction(wrapper ?? planner, thread, plan, null, answer);
                ScheduleContextKeeping(thread, request);
                return new ThreadResult(thread.Id, ThreadStatus.Completed, progress.Transcript, TotalTokens(thread));
            }

            if (executors.Count == 0) throw new OrchestrationException("Belum ada node Executor aktif. Tambahkan di menu Node.");

            await progress.LineAsync($"📋 Rencana: {plan.Summary}", cancellationToken);
            foreach (var task in plan.Tasks)
            {
                var dependencies = task.DependsOn is { Count: > 0 } deps ? $" (setelah {string.Join(", ", deps)})" : string.Empty;
                await progress.LineAsync($"   • {task.Id} `{task.Target}` — {task.Title}{dependencies}", cancellationToken);
            }
            recorder.Log(ActivityLevel.Info, $"Rencana: {plan.Tasks.Count} tugas, {plan.Contract.Count} item kontrak. {plan.Summary}");

            // 2. Execute in parallel; every write is journaled so the thread can be undone.
            await recorder.UpdateThreadAsync(t => t.Status = ThreadStatus.Executing);
            await progress.LineAsync($"⚙️ Menjalankan {plan.Tasks.Count} tugas paralel di {executors.Count} executor…", cancellationToken);
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
            var pipeline = new Pipeline(recorder, progress, request, project, executors, wrapper, wrapper ?? planner, scope, settings, journal, total, KeepContext: true);
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
            await progress.LineAsync($"🔁 Mengulang {retryIds.Count} tugas yang belum berhasil: {string.Join(", ", retryIds)}…", cancellationToken);

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

    // Verification (build and/or reviewer, with fix rounds), then the wrapper, then the thread is finished.
    // plan and execution cover every task the developer should hear about.
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

        var wrapClock = Stopwatch.StartNew();
        string finalText;
        var wrapTokens = 0;
        if (wrapper is null)
        {
            finalText = WrapperAgent.FallbackSummary(plan, execution, verification?.Report);
            await progress.FinalAsync(finalText, cancellationToken);
        }
        else
        {
            await progress.LineAsync($"📦 Wrapper `{wrapper.Model}` merangkum hasil…", cancellationToken);
            try
            {
                AiNode answered;
                (finalText, wrapTokens, answered) = await wrapperAgent.WrapAsync(wrapper, request.Prompt, plan, execution, verification?.ForModels,
                    delta => progress.FinalAsync(delta, cancellationToken), cancellationToken,
                    change => change.ReportAsync("Wrapper", recorder, progress, cancellationToken));
                if (answered.Id != wrapper.Id) await recorder.UpdateThreadAsync(t => t.WrapperModel = answered.Model);
                if (string.IsNullOrWhiteSpace(finalText))
                {
                    finalText = WrapperAgent.FallbackSummary(plan, execution, verification?.Report);
                    await progress.FinalAsync(finalText, cancellationToken);
                }
            }
            catch (NineRouterException ex)
            {
                recorder.Log(ActivityLevel.Warning, $"Wrapper gagal ({ex.Message}); memakai ringkasan otomatis.");
                finalText = WrapperAgent.FallbackSummary(plan, execution, verification?.Report);
                await progress.FinalAsync(finalText, cancellationToken);
            }
        }
        wrapClock.Stop();

        var succeeded = execution.Results.Count(r => r.Status == ThreadTaskStatus.Succeeded);
        var status = succeeded == 0 ? ThreadStatus.Failed : ThreadStatus.Completed;
        var notes = new List<string>();
        if (succeeded < execution.Results.Count) notes.Add($"{execution.Results.Count - succeeded} tugas tidak berhasil.");
        if (verification?.Report.Outcome == VerificationReport.Failed) notes.Add(VerificationNote(verification.Report));
        await FinishAsync(recorder, status, finalText, pipeline.Total, wrapTokens, wrapClock.ElapsedMilliseconds, notes.Count > 0 ? string.Join(" ", notes) : null);

        recorder.Log(status == ThreadStatus.Completed ? ActivityLevel.Success : ActivityLevel.Error,
            $"Thread selesai: {succeeded}/{execution.Results.Count} tugas berhasil dalam {Seconds(pipeline.Total.ElapsedMilliseconds)}.");

        if (succeeded > 0 && pipeline.MemoryNode is { } memoryNode) ScheduleMemoryExtraction(memoryNode, thread, plan, execution, finalText);
        if (pipeline.KeepContext) ScheduleContextKeeping(thread, request);
        return new ThreadResult(thread.Id, status, progress.Transcript, TotalTokens(thread));
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
            await MarkUnfinishedTasksCancelledAsync(thread.Id);
            await FinishAsync(recorder, ThreadStatus.Cancelled, null, total, 0, null, "Thread dibatalkan.");
            recorder.Log(ActivityLevel.Warning, "Thread dibatalkan.");
            return new ThreadResult(thread.Id, ThreadStatus.Cancelled, progress.Transcript, TotalTokens(thread));
        }
        catch (Exception ex)
        {
            if (ex is not (OrchestrationException or NineRouterException)) logger.LogError(ex, "Thread {ThreadId} crashed", thread.Id);
            var message = ex is OrchestrationException or NineRouterException ? ex.Message : $"Kesalahan internal: {ex.Message}";
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

    private static async Task ReportSpeedAsync(ClientProgress progress, ExecutionSummary execution, CancellationToken cancellationToken)
    {
        var speedup = execution.WallMs > 0 ? (double)execution.SumMs / execution.WallMs : 1;
        await progress.LineAsync(
            $"⏱️ Eksekusi selesai dalam {Seconds(execution.WallMs)} (total kerja executor {Seconds(execution.SumMs)} → {speedup:0.0}x lebih cepat dari sekuensial)",
            cancellationToken);
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
        var what = string.Join(" + ", new[] { build is null ? null : $"build `{build.Display}`", reviewer is null ? null : $"reviewer `{reviewer.Model}`" }.OfType<string>());
        await progress.LineAsync($"🔍 Verifikasi: {what}…", cancellationToken);

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
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Verification of thread {ThreadId} crashed", recorder.Thread.Id);
            recorder.Log(ActivityLevel.Error, $"Verifikasi gagal: {ex.Message}");
            await progress.LineAsync($"   ⚠️ Verifikasi gagal: {ex.Message}", cancellationToken);
            await recorder.UpdateThreadAsync(t => t.VerificationMs = clock.ElapsedMilliseconds);
            return null;
        }
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
        await progress.FinalAsync($"❌ AiThread gagal: {message}", CancellationToken.None);
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

    private void ScheduleMemoryExtraction(AiNode node, ThreadRun thread, ThreadPlan plan, ExecutionSummary? execution, string finalText) =>
        _ = Task.Run(() => memoryExtractor.ExtractAsync(node, thread, plan, execution, finalText, lifetime.ApplicationStopping), CancellationToken.None);

    // Only Claude Code chats carry thread anchors, so only their next turn can pick the pack up.
    private void ScheduleContextKeeping(ThreadRun thread, ThreadRequest request)
    {
        if (request.Source != ThreadSource.ClaudeCode) return;
        _ = Task.Run(() => contextKeeper.UpdateAsync(thread, request, lifetime.ApplicationStopping), CancellationToken.None);
    }

    private static int TotalTokens(ThreadRun thread) => thread.PlannerTokens + thread.ExecutorTokens + thread.ReviewerTokens + thread.WrapperTokens;

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
    // KeepContext updates the Claude Code context pack (not for retries, which no chat turn started).
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
        bool KeepContext);
}
