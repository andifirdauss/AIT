using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.UnitTests;
using AIThread.Service.Verification;
using AIThread.Service.Workspace;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    public async Task FinishAsync(UnitTestSession session, ThreadPlan plan, ExecutionSummary? execution, Guid threadId, ThreadRecorder recorder,
        CancellationToken cancellationToken)
    {
        try
        {
            if (session.BaselineRun is { } baselineRun) await baselineRun;
            var root = session.Project.RootPath;
            var now = DateTime.UtcNow;
            var keep = session.Settings.KeepSnapshots;
            var changed = execution?.Results.Any(r => r.Status == ThreadTaskStatus.Succeeded) ?? false;
            var (commit, branch) = keep && GitTools.IsRepository(root) ? await new GitTools(session.Scope).HeadAsync(cancellationToken) : (null, null);
            var updated = new HashSet<UnitTestCase>();
            var runs = new List<UnitTestRun>();
            var afterPhase = session.Report.TestOnly ? UnitTestRun.Manual : UnitTestRun.After;

            // Results: the run after the change wins; a test that only ran before it keeps that result (without a hash:
            // the code may have changed since).
            foreach (var test in session.Tests)
            {
                var after = session.After.GetValueOrDefault(test.Id);
                var baseline = session.Cached.ContainsKey(test.Id) ? null : session.Baseline.GetValueOrDefault(test.Id);
                if (baseline is { Remembered: false }) runs.Add(Run(session, test, UnitTestRun.Baseline, baseline, threadId));
                if (after is { Remembered: false }) runs.Add(Run(session, test, after.Flaky ? UnitTestRun.Flaky : afterPhase, after, threadId));
                var final = after ?? baseline;
                if (final is null || final.Remembered) continue;
                Apply(test, final, after is null ? null : UnitTestText.InputHash(root, test), now, session.Developer);
                if (final.Result == UnitTestResult.Passed && keep && after is not null)
                    UnitTestSnapshot.Capture(root, test, TestCommandResolver.SuiteFor(session.Suites, root, test.TestFile), commit, branch);
                updated.Add(test);
            }

            // Decisions of a change that happened: retired tests wait for the developer; unstable ones are quarantined.
            if (changed)
            {
                foreach (var decision in (plan.Tests?.Decisions ?? []).Where(d => d.Decision == PlanTests.Obsolete))
                {
                    if (session.ByNumber(decision.Test) is not { } test) continue;
                    test.State = UnitTestState.Obsolete;
                    test.ObsoleteReason = UnitTestText.Clip(decision.Reason, 1_000);
                    test.ObsoleteThreadId = threadId;
                    test.ObsoleteBy = session.Developer;
                    test.ObsoleteAt = now;
                    Item(session, test, isNew: false, PlanTests.Obsolete).Verdict = UnitTestReportItem.Retired;
                    updated.Add(test);
                }
            }
            foreach (var (id, reason) in session.Quarantine)
            {
                if (session.Tests.FirstOrDefault(t => t.Id == id) is not { } test) continue;
                test.State = UnitTestState.Quarantined;
                test.QuarantineReason = UnitTestText.Clip(reason, 1_000);
                updated.Add(test);
            }
            await catalog.UpdateAsync(updated.ToList(), cancellationToken);

            // The tests the change added join the catalog with their number, label and snapshot.
            if (changed && session.NewTests.Count > 0)
            {
                var paths = session.NewTests.SelectMany(t => UnitTestText.Lines(t.TargetFiles).Append(t.TestFile)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var modules = (await codeMapper.MapQuicklyAsync(session.Project, paths, cancellationToken))
                    .GroupBy(h => h.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Module, StringComparer.OrdinalIgnoreCase);
                foreach (var test in session.NewTests)
                {
                    test.Module = UnitTestText.Lines(test.TargetFiles).Select(modules.GetValueOrDefault).FirstOrDefault(m => m is not null) ?? modules.GetValueOrDefault(test.TestFile);
                    if (session.After.GetValueOrDefault(test.Id) is { } after)
                    {
                        Apply(test, after, UnitTestText.InputHash(root, test), now, session.Developer);
                        if (!after.Remembered) runs.Add(Run(session, test, after.Flaky ? UnitTestRun.Flaky : UnitTestRun.After, after, threadId));
                    }
                    if (keep) UnitTestSnapshot.Capture(root, test, TestCommandResolver.SuiteFor(session.Suites, root, test.TestFile), commit, branch);
                }
                await catalog.AddAsync(session.Project.Key, session.NewTests, cancellationToken);
                foreach (var test in session.NewTests.Where(t => session.Items.ContainsKey(t.Id)))
                {
                    session.Items[test.Id].Id = test.Id;
                    session.Items[test.Id].Number = test.DisplayNumber;
                }
                recorder.Log(ActivityLevel.Info, Lang.T($"Unit test baru masuk katalog: {string.Join(", ", session.NewTests.Select(t => $"{t.DisplayNumber} {t.Title}"))}.", $"New unit tests added to the catalog: {string.Join(", ", session.NewTests.Select(t => $"{t.DisplayNumber} {t.Title}"))}."));
            }
            await catalog.RecordRunsAsync(runs, cancellationToken);

            var report = session.Report;
            report.Outcome = report.Tests.Any(i => i.IsProblem || i.Verdict == UnitTestReportItem.Failing) ? UnitTestReport.Failed
                : report.Tests.Any(i => i.After is not null) ? UnitTestReport.Passed
                : UnitTestReport.Skipped;
            report.Summary = Summary(report);
            if (report.Tests.Count == 0 && report.Restored.Count == 0 && report.Notes.Count == 0) return;
            await recorder.UpdateThreadAsync(t =>
            {
                t.UnitTestJson = report.ToJson();
                t.UnitTestMs = report.BaselineMs + report.Rounds.Sum(r => r.DurationMs);
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The unit test results of thread {ThreadId} could not be saved.", threadId);
            recorder.Log(ActivityLevel.Warning, Lang.T($"Hasil unit test tidak bisa disimpan ke katalog: {ex.Message}", $"Unit test results cannot be saved to the catalog: {ex.Message}"));
        }
    }
}
