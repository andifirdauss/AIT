using System.Diagnostics;
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
        try
        {
            var project = prepared.Project;
            await progress.AnchorAsync(ThreadAnchor.Line(project.Name, thread.Id), cancellationToken);

            // Extra active planners are fallbacks for when the preferred one is rate limited or down.
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
                await progress.LineAsync($"   • {task.Id} `{task.File}` — {task.Title}{dependencies}", cancellationToken);
            }
            recorder.Log(ActivityLevel.Info, $"Rencana: {plan.Tasks.Count} tugas, {plan.Contract.Count} item kontrak. {plan.Summary}");

            // 2. Execute in parallel
            await recorder.UpdateThreadAsync(t => t.Status = ThreadStatus.Executing);
            await progress.LineAsync($"⚙️ Menjalankan {plan.Tasks.Count} tugas paralel di {executors.Count} executor…", cancellationToken);
            var taskEntities = await recorder.CreateTasksAsync(plan);
            var execution = await executionEngine.RunAsync(new ExecutionRequest(plan, taskEntities, executors, scope, settings), recorder, progress, cancellationToken);

            await recorder.UpdateThreadAsync(t =>
            {
                t.ExecutionWallMs = execution.WallMs;
                t.ExecutionSumMs = execution.SumMs;
                t.ExecutorTokens = execution.Tokens;
            });
            var speedup = execution.WallMs > 0 ? (double)execution.SumMs / execution.WallMs : 1;
            await progress.LineAsync(
                $"⏱️ Eksekusi selesai dalam {Seconds(execution.WallMs)} (total kerja executor {Seconds(execution.SumMs)} → {speedup:0.0}x lebih cepat dari sekuensial)",
                cancellationToken);

            // 3. Verify: build and/or review, with fix rounds. From here on plan and execution include the fix tasks.
            var verification = await VerifyAsync(request, project, plan, execution, executors, scope, settings, recorder, progress, cancellationToken);
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
                    (finalText, wrapTokens) = await wrapperAgent.WrapAsync(wrapper, request.Prompt, plan, execution, verification?.ForModels,
                        delta => progress.FinalAsync(delta, cancellationToken), cancellationToken);
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
            await FinishAsync(recorder, status, finalText, total, wrapTokens, wrapClock.ElapsedMilliseconds, notes.Count > 0 ? string.Join(" ", notes) : null);

            recorder.Log(status == ThreadStatus.Completed ? ActivityLevel.Success : ActivityLevel.Error,
                $"Thread selesai: {succeeded}/{execution.Results.Count} tugas berhasil dalam {Seconds(total.ElapsedMilliseconds)}.");

            if (succeeded > 0) ScheduleMemoryExtraction(wrapper ?? planner, thread, plan, execution, finalText);
            ScheduleContextKeeping(thread, request);
            return new ThreadResult(thread.Id, status, progress.Transcript, TotalTokens(thread));
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
            ScheduleContextKeeping(thread, request);
            return failed;
        }
        finally
        {
            registry.Complete(thread.Id);
        }
    }

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
        ThreadRequest request,
        ProjectContext project,
        ThreadPlan plan,
        ExecutionSummary execution,
        IReadOnlyList<AiNode> executors,
        WorkspaceScope scope,
        RuntimeSettings settings,
        ThreadRecorder recorder,
        ClientProgress progress,
        CancellationToken cancellationToken)
    {
        if (!execution.Results.Any(r => r.Status == ThreadTaskStatus.Succeeded)) return null;

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
                new VerificationInput(request, plan, execution, build, reviewer, executors, scope, settings, plan.Tasks.Count + 1),
                recorder, progress, cancellationToken);
            clock.Stop();

            await recorder.UpdateThreadAsync(t =>
            {
                t.VerificationMs = clock.ElapsedMilliseconds;
                t.VerificationJson = result.Report.ToJson();
                t.ReviewerTokens = result.ReviewerTokens;
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
            t.WrapperTokens = wrapTokens;
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
}
