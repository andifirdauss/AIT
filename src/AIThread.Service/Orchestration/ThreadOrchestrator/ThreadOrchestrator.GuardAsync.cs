using System.Diagnostics;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Models;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
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
            await FinishAsync(recorder, ThreadStatus.Cancelled, null, total, 0, null, Lang.T("Thread dibatalkan.", "Thread cancelled."));
            recorder.Log(ActivityLevel.Warning, Lang.T("Thread dibatalkan.", "Thread cancelled."));
            return new ThreadResult(thread.Id, ThreadStatus.Cancelled, progress.Transcript, TotalTokens(thread));
        }
        catch (Exception ex)
        {
            if (ex is not (OrchestrationException or NineRouterException)) logger.LogError(ex, "Thread {ThreadId} crashed", thread.Id);
            var message = ex is OrchestrationException or NineRouterException ? ex.Message : Lang.T($"Kesalahan internal: {ex.Message}", $"Internal error: {ex.Message}");
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
}
