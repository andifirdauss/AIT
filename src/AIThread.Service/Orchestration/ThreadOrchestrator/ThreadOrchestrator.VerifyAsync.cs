using System.Diagnostics;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Verification;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // Runs when something was changed and there is a way to check it: a build command (and building is allowed), an
    // active Reviewer node or the project's unit tests. Null when skipped. A crash in here only costs the verification,
    // not the edits already made.
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
        // The solution that holds the changed files, which is not always the repository's main one.
        var changed = plan.Tasks.SelectMany(t => new[] { t.File, t.To }).OfType<string>();
        var build = settings.BuildEnabled ? BuildCommandResolver.ForChanges(project.RootPath, project.Modules, changed, out _) : null;
        if (build is null && reviewer is null && pipeline.UnitTests is null) return null;

        await recorder.UpdateThreadAsync(t =>
        {
            t.Status = ThreadStatus.Verifying;
            t.ReviewerModel = reviewer?.Model;
        });
        var what = string.Join(" + ", new[] { build?.Display, reviewer is null ? null : $"reviewer {reviewer.Model}", pipeline.UnitTests is null ? null : "unit test" }.OfType<string>());
        await progress.PhaseAsync("🔍", Lang.T($"Verifikasi · {what}", $"Verification · {what}"), cancellationToken);

        var clock = Stopwatch.StartNew();
        try
        {
            var result = await verificationStage.RunAsync(
                new VerificationInput(pipeline.Request, plan, execution, build, reviewer, pipeline.Executors, pipeline.Scope, settings, nextTaskOrder,
                    pipeline.Journal, firstRound, pipeline.UnitTests, pipeline.Triage),
                recorder, progress, cancellationToken);
            clock.Stop();

            await recorder.UpdateThreadAsync(t =>
            {
                t.VerificationMs = clock.ElapsedMilliseconds;
                t.VerificationJson = result.Report.ToJson();
                t.ReviewerTokens += result.ReviewerTokens;
                t.ExecutorTokens += result.ExecutorTokens;
                t.TesterTokens += result.TesterTokens;
                if (result.TesterTokens > 0) t.TesterModel ??= pipeline.Triage?.Model;
            });
            await progress.StepAsync(VerificationClosing(result.Report), cancellationToken, last: true);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Verification of thread {ThreadId} crashed", recorder.Thread.Id);
            recorder.Log(ActivityLevel.Error, Lang.T($"Verifikasi gagal: {ex.Message}", $"Verification failed: {ex.Message}"));
            await progress.StepAsync(Lang.T($"🟡 Verifikasi gagal: {ex.Message}", $"🟡 Verification failed: {ex.Message}"), cancellationToken, hang: 3);
            await progress.StepAsync(Lang.T("verifikasi dihentikan", "verification stopped"), cancellationToken, last: true);
            await recorder.UpdateThreadAsync(t => t.VerificationMs = clock.ElapsedMilliseconds);
            return null;
        }
    }
}
