using System.Diagnostics;
using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Data.Context;
using AiThread.Data.Messaging;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Orchestration;

// planner (expensive, explores + plans) -> executors (cheap, parallel, one file each) -> wrapper (pools and reports).
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
    WrapperAgent wrapperAgent,
    MemoryExtractor memoryExtractor,
    IHostApplicationLifetime lifetime,
    ILogger<ThreadOrchestrator> logger) : IThreadOrchestrator
{
    public async Task<ThreadResult> RunAsync(ThreadRequest request, IThreadObserver observer, CancellationToken cancellationToken)
    {
        var prepared = await PrepareAsync(request, cancellationToken);
        return await ExecuteAsync(prepared, request, observer, cancellationToken);
    }

    public async Task<Guid> StartInBackgroundAsync(ThreadRequest request, CancellationToken cancellationToken)
    {
        var prepared = await PrepareAsync(request, cancellationToken);
        _ = Task.Run(() => ExecuteAsync(prepared, request, NullThreadObserver.Instance, lifetime.ApplicationStopping), CancellationToken.None);
        return prepared.Thread.Id;
    }

    private async Task<PreparedThread> PrepareAsync(ThreadRequest request, CancellationToken cancellationToken)
    {
        var project = request.ProjectId is { } projectId
            ? await projectService.GetContextAsync(projectId, cancellationToken)
            : await projectService.ResolveAsync(request.WorkingDirectory, request.SolutionHint, cancellationToken);

        var thread = new ThreadRun
        {
            ProjectId = project?.ProjectId,
            Source = request.Source,
            SessionId = request.SessionId,
            WorkingDirectory = Truncate(request.WorkingDirectory ?? project?.RootPath, 800),
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
            $"Thread baru dari {(request.Source == ThreadSource.ClaudeCode ? "Claude Code" : "dashboard")} · {project?.Name ?? "tanpa project"}: {Truncate(request.Prompt.ReplaceLineEndings(" "), 160)}",
            thread.Id, thread.ProjectId);

        return new PreparedThread(thread, project);
    }

    private async Task<ThreadResult> ExecuteAsync(PreparedThread prepared, ThreadRequest request, IThreadObserver observer, CancellationToken externalToken)
    {
        var thread = prepared.Thread;
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
            var project = prepared.Project
                          ?? throw new OrchestrationException("Working directory tidak terdeteksi dari request, sehingga project tidak dapat ditentukan.");

            var solutionLabel = project.SolutionPath is null ? "⚠️ tanpa .sln/.slnx (memory project nonaktif)" : $"`{Path.GetFileName(project.SolutionPath)}`";
            await progress.LineAsync($"**AiThread** · {solutionLabel} · thread `{thread.Id.ToString()[..8]}`", cancellationToken);

            var planner = (await nodeService.GetActiveAsync(NodeRole.Planner, cancellationToken)).FirstOrDefault()
                          ?? throw new OrchestrationException("Belum ada node Planner aktif. Tambahkan di menu Node.");
            var executors = await nodeService.GetActiveAsync(NodeRole.Executor, cancellationToken);
            var wrapper = (await nodeService.GetActiveAsync(NodeRole.Wrapper, cancellationToken)).FirstOrDefault();

            // 1. Plan
            await recorder.UpdateThreadAsync(t =>
            {
                t.Status = ThreadStatus.Planning;
                t.PlannerModel = planner.Model;
            });
            await progress.LineAsync($"🧠 Planner `{planner.Model}` menganalisis project & menyusun rencana…", cancellationToken);

            var scope = new WorkspaceScope(project.RootPath);
            var planClock = Stopwatch.StartNew();
            var planning = await plannerAgent.PlanAsync(new PlannerInput(request, project, planner, scope, settings), recorder, progress, cancellationToken);
            planClock.Stop();

            var plan = planning.Plan;
            await recorder.UpdateThreadAsync(t =>
            {
                t.PlanJson = plan.ToJson();
                t.PlanSummary = Truncate(plan.Summary, 2000);
                t.PlannerSteps = planning.Steps;
                t.PlannerTokens = planning.Tokens;
                t.PlanningMs = planClock.ElapsedMilliseconds;
            });

            if (plan.Tasks.Count == 0)
            {
                var answer = string.IsNullOrWhiteSpace(plan.Answer) ? plan.Summary : plan.Answer;
                await progress.FinalAsync(answer, cancellationToken);
                recorder.Log(ActivityLevel.Success, "Planner menjawab langsung tanpa tugas eksekusi.");
                await FinishAsync(recorder, ThreadStatus.Completed, answer, total, 0, null);
                ScheduleMemoryExtraction(wrapper ?? planner, thread, plan, null, answer);
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

            // 3. Wrap
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
                finalText = WrapperAgent.FallbackSummary(plan, execution);
                await progress.FinalAsync(finalText, cancellationToken);
            }
            else
            {
                await progress.LineAsync($"📦 Wrapper `{wrapper.Model}` merangkum hasil…", cancellationToken);
                try
                {
                    (finalText, wrapTokens) = await wrapperAgent.WrapAsync(wrapper, request.Prompt, plan, execution,
                        delta => progress.FinalAsync(delta, cancellationToken), cancellationToken);
                    if (string.IsNullOrWhiteSpace(finalText))
                    {
                        finalText = WrapperAgent.FallbackSummary(plan, execution);
                        await progress.FinalAsync(finalText, cancellationToken);
                    }
                }
                catch (NineRouterException ex)
                {
                    recorder.Log(ActivityLevel.Warning, $"Wrapper gagal ({ex.Message}); memakai ringkasan otomatis.");
                    finalText = WrapperAgent.FallbackSummary(plan, execution);
                    await progress.FinalAsync(finalText, cancellationToken);
                }
            }
            wrapClock.Stop();

            var succeeded = execution.Results.Count(r => r.Status == ThreadTaskStatus.Succeeded);
            var status = succeeded == 0 ? ThreadStatus.Failed : ThreadStatus.Completed;
            await FinishAsync(recorder, status, finalText, total, wrapTokens, wrapClock.ElapsedMilliseconds,
                succeeded < execution.Results.Count ? $"{execution.Results.Count - succeeded} tugas tidak berhasil." : null);

            recorder.Log(status == ThreadStatus.Completed ? ActivityLevel.Success : ActivityLevel.Error,
                $"Thread selesai: {succeeded}/{execution.Results.Count} tugas berhasil dalam {Seconds(total.ElapsedMilliseconds)}.");

            if (succeeded > 0) ScheduleMemoryExtraction(wrapper ?? planner, thread, plan, execution, finalText);
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
            return await FailAsync(recorder, progress, total, message);
        }
        finally
        {
            registry.Complete(thread.Id);
        }
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
            t.TotalMs = total.ElapsedMilliseconds;
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

    private static int TotalTokens(ThreadRun thread) => thread.PlannerTokens + thread.ExecutorTokens + thread.WrapperTokens;

    private static string Seconds(long milliseconds) => $"{milliseconds / 1000.0:0.0}s";

    private static string Truncate(string? value, int max) =>
        value is null ? string.Empty : value.Length <= max ? value : value[..max];

    private sealed record PreparedThread(ThreadRun Thread, ProjectContext? Project);
}
