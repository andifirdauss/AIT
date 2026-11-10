using AIThread.Common.Enums;

namespace AIThread.Service.Orchestration;

public sealed partial class WrapperAgent
{
    // Nothing to explain: at most maxTasks tasks, all done, verified without a fix round, no test that failed or could
    // not run, and a prompt that asks for no explanation. Anything else keeps the wrapper, whose job is then to say what
    // is left to do or to answer the developer.
    public static bool CanSkip(int maxTasks, string prompt, ExecutionSummary execution, VerificationReport? verification, TestingReport? testing, UnitTestReport? unitTests) =>
        execution.Results.Count <= maxTasks
        && execution.Results.All(r => r.Status == ThreadTaskStatus.Succeeded)
        && verification is null or { Outcome: VerificationReport.Passed, Rounds.Count: 1 }
        && testing?.Outcome is not (TestingReport.Failed or TestingReport.Blocked)
        && unitTests?.Outcome != UnitTestReport.Failed
        && !AsksForExplanation(prompt);
}
