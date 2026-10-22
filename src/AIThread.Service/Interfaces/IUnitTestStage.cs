using AIThread.Service.Models;
using AIThread.Service.Orchestration;

namespace AIThread.Service.Interfaces;

public interface IUnitTestStage
{
    // The thread's session with the project's catalog; null when the project runs no unit tests (setting Off, no project
    // key, no test suite AIT knows). The first thread of a project with tests fills the catalog from the code.
    Task<UnitTestSession?> OpenAsync(ProjectContext project, RuntimeSettings settings, CancellationToken cancellationToken);

    // Puts missing tests back before planning; returns the executor tasks for what needs a model.
    Task<IReadOnlyList<PlanTask>> RestoreAsync(UnitTestSession session, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken);

    // Runs the tests related to these files and modules in the background while the planner plans (nothing changed yet).
    void StartBaseline(UnitTestSession session, IReadOnlyCollection<string> files, IReadOnlyCollection<string> modules, ThreadRecorder recorder, CancellationToken cancellationToken);

    // After planning, before the executors: waits for the baseline and runs the tests the plan concerns that it missed.
    Task CompleteBaselineAsync(UnitTestSession session, ThreadPlan plan, IReadOnlyCollection<string> modules, ThreadRecorder recorder, ClientProgress progress,
        CancellationToken cancellationToken);

    // After a build that passes: runs the tests again, compares with the baseline, has the tester triage what fails and
    // returns what to fix.
    Task<UnitTestCheck> CheckAsync(UnitTestSession session, UnitTestCheckInput input, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken);

    // A thread that only asked for tests: runs them (no baseline).
    Task RunOnlyAsync(UnitTestSession session, PlanTests tests, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken);

    // The planner's run_tests: results as the planner reads them, and a headline for the progress. Runs before the
    // change also count as baseline.
    Task<(string ForModel, string Headline)> RunForPlannerAsync(UnitTestSession session, IReadOnlyList<string> numbers, string? file, bool all, CancellationToken cancellationToken);

    // End of the thread: results, new tests, obsolete decisions, quarantines and snapshots go to the catalog, the report
    // to the thread. Never fails the thread.
    Task FinishAsync(UnitTestSession session, ThreadPlan plan, ExecutionSummary? execution, Guid threadId, ThreadRecorder recorder, CancellationToken cancellationToken);
}
