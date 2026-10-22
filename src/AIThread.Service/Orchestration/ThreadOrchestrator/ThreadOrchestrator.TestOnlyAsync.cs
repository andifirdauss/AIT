using System.Diagnostics;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // The developer only asked for a test: the plan has no tasks, but unit tests to run (tests.run) and/or a check of
    // the running app (test). Nothing is changed, built or reviewed; the wrapper explains the results.
    private async Task<ThreadResult> TestOnlyAsync(Pipeline pipeline, ThreadPlan plan, CancellationToken cancellationToken)
    {
        var (recorder, progress, request, wrapper) = (pipeline.Recorder, pipeline.Progress, pipeline.Request, pipeline.Wrapper);
        var thread = recorder.Thread;
        var unit = pipeline.UnitTests;
        recorder.Log(ActivityLevel.Info, "Planner tidak mengubah kode; developer hanya meminta pengujian.");

        if (unit is not null && plan.Tests is { Run.Count: > 0 } tests)
        {
            await recorder.UpdateThreadAsync(t => t.Status = ThreadStatus.Testing);
            await unitTestStage.RunOnlyAsync(unit, tests, recorder, progress, cancellationToken);
        }
        if (unit is not null) await unitTestStage.FinishAsync(unit, plan, null, thread.Id, recorder, cancellationToken);
        var unitReport = unit?.Report is { Tests.Count: > 0 } report ? report : null;

        var execution = new ExecutionSummary([], 0, 0, 0);
        var testing = plan.Test is { IsRunnable: true } ? await TestAsync(pipeline, plan, execution, null, cancellationToken, testOnly: true) : null;

        await recorder.UpdateThreadAsync(t =>
        {
            t.Status = ThreadStatus.Wrapping;
            t.WrapperModel = wrapper?.Model;
        });
        if (wrapper is not null) await progress.PhaseAsync("📦", $"Rangkuman · {wrapper.Model}", cancellationToken);
        await progress.FinalAsync(FinalReport.TestOnlyStatus(unitReport, testing?.Report), cancellationToken);

        var unitText = "No file was changed: the developer only asked for a test." + (unitReport is null ? string.Empty : "\n" + UnitTestStage.Describe(unitReport));
        var wrapClock = Stopwatch.StartNew();
        string finalText;
        var wrapTokens = 0;
        var narrated = false;
        var fallback = (plan.Answer ?? plan.Summary) + (unitReport is null ? string.Empty : $"\n\n**Unit test:** {unitReport.Summary}")
                       + (testing is null ? string.Empty : $"\n\n**Testing:** {FinalReport.TestingLabel(testing.Report)}" + (testing.Report.Summary is null ? string.Empty : $" — {testing.Report.Summary}"));
        if (wrapper is null)
        {
            finalText = fallback;
            await progress.FinalAsync(plan.Answer ?? plan.Summary, cancellationToken);
        }
        else
        {
            try
            {
                AiNode answered;
                (finalText, wrapTokens, answered) = await wrapperAgent.WrapAsync(wrapper, request.Prompt, plan, execution, unitText,
                    delta =>
                    {
                        narrated = true;
                        return progress.FinalAsync(delta, cancellationToken);
                    }, cancellationToken,
                    change => change.ReportAsync("Wrapper", recorder, progress, cancellationToken), testing?.ForModels);
                if (answered.Id != wrapper.Id) await recorder.UpdateThreadAsync(t => t.WrapperModel = answered.Model);
                if (string.IsNullOrWhiteSpace(finalText))
                {
                    finalText = fallback;
                    if (!narrated) await progress.FinalAsync(plan.Answer ?? plan.Summary, cancellationToken);
                }
            }
            catch (NineRouterException ex)
            {
                recorder.Log(ActivityLevel.Warning, $"Wrapper gagal ({ex.Message}); memakai ringkasan otomatis.");
                finalText = fallback;
                if (!narrated) await progress.FinalAsync(plan.Answer ?? plan.Summary, cancellationToken);
            }
        }
        wrapClock.Stop();
        if (unitReport is not null) await progress.FinalAsync(FinalReport.UnitTests(unitReport), cancellationToken);
        if (testing is not null) await progress.FinalAsync(FinalReport.Testing(testing.Report), cancellationToken);

        var notes = new List<string>();
        if (unitReport?.Outcome == UnitTestReport.Failed) notes.Add(UnitTestNote(unitReport));
        if (testing?.Report.Outcome == TestingReport.Failed) notes.Add(TestingNote(testing.Report));
        await FinishAsync(recorder, ThreadStatus.Completed, finalText, pipeline.Total, wrapTokens, wrapClock.ElapsedMilliseconds, notes.Count > 0 ? string.Join(" ", notes) : null);
        recorder.Log(ActivityLevel.Success, $"Thread selesai (hanya pengujian) dalam {Seconds(pipeline.Total.ElapsedMilliseconds)}.");

        await FooterAsync(progress, request, pipeline.Settings, thread, ThreadStatus.Completed, pipeline.Total, pipeline.MemoriesUsed, null, false, cancellationToken);
        if (pipeline.KeepContext) ScheduleContextKeeping(thread, request);
        return new ThreadResult(thread.Id, ThreadStatus.Completed, progress.Transcript, TotalTokens(thread));
    }
}
