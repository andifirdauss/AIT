using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.UnitTests;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    public async Task RunOnlyAsync(UnitTestSession session, PlanTests tests, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        session.Report.TestOnly = true;
        var selected = tests.Run.Contains(PlanTests.All, StringComparer.OrdinalIgnoreCase)
            ? session.Active.ToList()
            : tests.Run.Select(session.ByNumber).OfType<UnitTestCase>().Distinct().ToList();
        await progress.PhaseAsync("🧪", $"Unit test · {selected.Count} test", cancellationToken);
        if (selected.Count == 0 || !session.CanRun)
        {
            await progress.StepAsync(session.CanRun ? "tidak ada test yang dijalankan" : "tidak ada runner test yang bisa dijalankan di mesin ini", cancellationToken, last: true);
            return;
        }

        var verdicts = new Dictionary<Guid, TestVerdict>();
        var toRun = new List<UnitTestCase>();
        var reused = 0;
        foreach (var test in selected)
        {
            if (EnvironmentSkip(session, test) is { } skip) verdicts[test.Id] = skip;
            // Nothing changed since the planner's run_tests (or the baseline) ran it in this thread: that result is
            // this run's. It moves out of the baseline so the catalog records the run once, as manual.
            else if (!session.Cached.ContainsKey(test.Id) && session.Baseline.TryRemove(test.Id, out var earlier) && !earlier.Remembered)
            {
                verdicts[test.Id] = earlier;
                reused++;
            }
            else toRun.Add(test);
        }
        var round = new UnitTestRoundReport { Round = 1, Ran = toRun.Count };
        session.Report.Rounds.Add(round);
        if (toRun.Count > 0)
        {
            var execution = await executor.RunAsync(session.Project.RootPath, session.Suites, toRun, session.Timeout, retryFailures: true, cancellationToken);
            foreach (var (id, verdict) in execution.Verdicts) verdicts[id] = verdict;
            round.DurationMs = execution.DurationMs;
            round.Commands = execution.Suites.Select(s => s.Display).ToList();
        }

        foreach (var test in selected)
        {
            var verdict = verdicts[test.Id];
            session.After[test.Id] = verdict;
            var item = Item(session, test, isNew: false, decision: null);
            item.After = Lower(verdict.Result);
            item.Message = verdict.Message is null ? null : UnitTestText.Clip(verdict.Message, MaxMessageChars);
            item.SkipReason = verdict.SkipReason;
            item.DurationMs = verdict.DurationMs;
            item.Verdict = verdict.Result switch
            {
                UnitTestResult.Passed => verdict.Flaky ? UnitTestReportItem.Flaky : UnitTestReportItem.Ok,
                UnitTestResult.Failed or UnitTestResult.Error => UnitTestReportItem.Failing,
                UnitTestResult.NotFound => UnitTestReportItem.NotFound,
                _ => UnitTestReportItem.SkippedVerdict
            };
        }
        round.Passed = selected.Count(t => verdicts[t.Id].Result == UnitTestResult.Passed);
        round.Failed = selected.Count(t => verdicts[t.Id].IsFailure);
        round.Skipped = selected.Count - round.Passed - round.Failed;
        round.Regressions = selected.Where(t => verdicts[t.Id].IsFailure).Select(t => t.DisplayNumber).ToList();

        var icon = round.Failed > 0 ? "🔴" : round.Skipped > 0 ? "🟡" : "✅";
        await progress.StepAsync($"{icon} {round.Passed} lolos" + (round.Failed > 0 ? $", {round.Failed} gagal" : string.Empty)
                                 + (round.Skipped > 0 ? $", {round.Skipped} dilewati" : string.Empty)
                                 + (toRun.Count > 0 ? $" ({round.DurationMs / 1000.0:0.0}s)" : string.Empty)
                                 + (reused > 0 ? $" · {reused} dari run_tests planner" : string.Empty), cancellationToken, last: true);
        foreach (var test in selected.Where(t => verdicts[t.Id].IsFailure).Take(MaxProgressFailures))
            await progress.DetailAsync($"{test.DisplayNumber} {test.Title} — {OneLine(verdicts[test.Id].Message)}", cancellationToken, underLast: true);
        recorder.Log(round.Failed > 0 ? ActivityLevel.Warning : ActivityLevel.Success,
            $"Unit test (tanpa perubahan kode): {selected.Count} test, {round.Passed} lolos, {round.Failed} gagal, {round.Skipped} dilewati.");
    }
}
