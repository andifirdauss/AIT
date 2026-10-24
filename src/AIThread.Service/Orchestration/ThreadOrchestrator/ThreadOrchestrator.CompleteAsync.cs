using System.Diagnostics;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // Verification (build and/or reviewer, with fix rounds), testing (Auto Testing), then the final answer, then the
    // thread is finished. plan and execution cover every task the developer should hear about. The answer is the status
    // (FinalReport), the wrapper's text, the changed files, the test with its API output and, once the memory extraction
    // is done, the footer; the thread stores only the wrapper's text (or the automatic summary) as its final response.
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

        // 4. Test: the tester node uses the running app the way the plan's test section describes.
        var testing = await TestAsync(pipeline, plan, execution, verification, cancellationToken);

        // The unit test catalog learns the results, the new tests and the decisions of this change.
        var unitReport = pipeline.UnitTests is { } unitTests ? await FinishUnitTestsAsync(unitTests, plan, execution, recorder, cancellationToken) : null;

        // 5. Wrap
        await recorder.UpdateThreadAsync(t =>
        {
            t.Status = ThreadStatus.Wrapping;
            t.WrapperModel = wrapper?.Model;
        });

        if (wrapper is not null) await progress.PhaseAsync("📦", $"Rangkuman · {wrapper.Model}", cancellationToken);
        await progress.FinalAsync(FinalReport.Status(execution, verification?.Report, testing?.Report, unitReport,
            retryHint: request.Source == ThreadSource.ClaudeCode), cancellationToken);

        // Without the wrapper's text the developer still gets the plan summary between the status and the files.
        var wrapClock = Stopwatch.StartNew();
        string finalText;
        var wrapTokens = 0;
        var narrated = false;
        if (wrapper is null)
        {
            finalText = WrapperAgent.FallbackSummary(plan, execution, verification?.Report, testing?.Report, unitReport);
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
                    change => change.ReportAsync("Wrapper", recorder, progress, cancellationToken), testing?.ForModels);
                if (answered.Id != wrapper.Id) await recorder.UpdateThreadAsync(t => t.WrapperModel = answered.Model);
                if (string.IsNullOrWhiteSpace(finalText))
                {
                    finalText = WrapperAgent.FallbackSummary(plan, execution, verification?.Report, testing?.Report, unitReport);
                    if (!narrated) await progress.FinalAsync(plan.Summary, cancellationToken);
                }
            }
            catch (NineRouterException ex)
            {
                recorder.Log(ActivityLevel.Warning, $"Wrapper gagal ({ex.Message}); memakai ringkasan otomatis.");
                finalText = WrapperAgent.FallbackSummary(plan, execution, verification?.Report, testing?.Report, unitReport);
                if (!narrated) await progress.FinalAsync(plan.Summary, cancellationToken);
            }
        }
        wrapClock.Stop();
        await progress.FinalAsync(FinalReport.ChangedFiles(execution), cancellationToken);
        if (unitReport is not null) await progress.FinalAsync(FinalReport.UnitTests(unitReport), cancellationToken);
        if (testing is not null) await progress.FinalAsync(FinalReport.Testing(testing.Report), cancellationToken);

        var succeeded = execution.Results.Count(r => r.Status == ThreadTaskStatus.Succeeded);
        var status = succeeded == 0 ? ThreadStatus.Failed : ThreadStatus.Completed;
        var notes = new List<string>();
        if (succeeded < execution.Results.Count) notes.Add($"{execution.Results.Count - succeeded} tugas tidak berhasil.");
        if (verification?.Report.Outcome == VerificationReport.Failed) notes.Add(VerificationNote(verification.Report));
        if (testing?.Report.Outcome == TestingReport.Failed) notes.Add(TestingNote(testing.Report));
        if (unitReport?.Outcome == UnitTestReport.Failed) notes.Add(UnitTestNote(unitReport));
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
}
