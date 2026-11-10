using AIThread.Common.Enums;

namespace AIThread.Service.Orchestration;

public sealed partial class WrapperAgent
{
    // Nothing to explain: at most maxTasks tasks, all done, verified without a fix round, and no test that failed or
    // could not run. Anything else keeps the wrapper, whose job is then to say what is left to do.
    public static bool CanSkip(int maxTasks, ExecutionSummary execution, VerificationReport? verification, TestingReport? testing, UnitTestReport? unitTests) =>
        execution.Results.Count <= maxTasks
        && execution.Results.All(r => r.Status == ThreadTaskStatus.Succeeded)
        && verification is null or { Outcome: VerificationReport.Passed, Rounds.Count: 1 }
        && testing?.Outcome is not (TestingReport.Failed or TestingReport.Blocked)
        && unitTests?.Outcome != UnitTestReport.Failed;
}
