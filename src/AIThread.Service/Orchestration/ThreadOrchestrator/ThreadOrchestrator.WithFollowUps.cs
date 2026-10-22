namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // A copy of a follow-up's report that lists the thread's suggestions (for the answer only; not stored that way).
    private static TestingReport WithFollowUps(TestingReport run, TestingReport parent)
    {
        var copy = TestingReport.FromJson(run.ToJson())!;
        copy.FollowUps = parent.FollowUps.Select(f => new TestFollowUp
        {
            Number = f.Number, Title = f.Title, Why = f.Why, Needs = f.Needs, Scenarios = f.Scenarios, LastRunAt = f.LastRunAt, ScenarioId = f.ScenarioId,
            LastRun = f.LastRun is null ? null : new TestingReport { Outcome = f.LastRun.Outcome, Level = f.LastRun.Level, Scenarios = f.LastRun.Scenarios, LoginNeeded = f.LastRun.LoginNeeded }
        }).ToList();
        return copy;
    }
}
