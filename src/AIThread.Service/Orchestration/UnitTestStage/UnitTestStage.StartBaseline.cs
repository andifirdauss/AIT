using AIThread.Service.UnitTests;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    // No progress lines: the planner's phase is on screen meanwhile. CompleteBaselineAsync reports the baseline.
    public void StartBaseline(UnitTestSession session, IReadOnlyCollection<string> files, IReadOnlyCollection<string> modules, ThreadRecorder recorder,
        CancellationToken cancellationToken)
    {
        if (!session.CanRun) return;
        var relevant = UnitTestSelection.Relevant(session.Tests, session.Settings.Mode, files, modules);
        foreach (var test in relevant) session.Relevant.Add(test.Id);
        if (relevant.Count == 0) return;
        session.BaselineRun = Task.Run(() => BaselineAsync(session, relevant, recorder, cancellationToken), CancellationToken.None);
    }
}
