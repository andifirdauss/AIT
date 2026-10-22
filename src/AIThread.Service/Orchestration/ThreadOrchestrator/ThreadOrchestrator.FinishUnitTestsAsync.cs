namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // The report for the answer, or null when no unit test ran and none was restored.
    private async Task<UnitTestReport?> FinishUnitTestsAsync(UnitTestSession unitTests, ThreadPlan plan, ExecutionSummary execution, ThreadRecorder recorder,
        CancellationToken cancellationToken)
    {
        await unitTestStage.FinishAsync(unitTests, plan, execution, recorder.Thread.Id, recorder, cancellationToken);
        return unitTests.Report.Tests.Count > 0 || unitTests.Report.Restored.Count > 0 ? unitTests.Report : null;
    }
}
