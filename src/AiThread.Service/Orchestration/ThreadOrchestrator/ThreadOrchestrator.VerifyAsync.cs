using System.Diagnostics;
using AiThread.Common.Enums;
using AiThread.Service.Verification;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
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
}
