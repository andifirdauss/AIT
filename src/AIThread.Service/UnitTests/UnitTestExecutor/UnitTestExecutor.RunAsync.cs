using System.Diagnostics;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Verification;

namespace AIThread.Service.UnitTests;

public sealed partial class UnitTestExecutor
{
    public async Task<UnitTestExecution> RunAsync(string root, IReadOnlyList<TestSuite> suites, IReadOnlyList<UnitTestCase> tests, TimeSpan timeout,
        bool retryFailures, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var verdicts = new Dictionary<Guid, TestVerdict>();
        var runs = new List<SuiteRun>();
        foreach (var (suite, group) in Group(root, suites, tests, verdicts))
        {
            var (run, groupVerdicts, perTest) = await RunSuiteAsync(suite, group, timeout, cancellationToken);
            runs.Add(run);
            foreach (var (id, verdict) in groupVerdicts) verdicts[id] = verdict;

            // A failure that passes on a second run is unstable, not broken. Only failures of single tests are tried
            // again: a suite that did not run at all (build error) would fail the same way.
            var failing = group.Where(t => verdicts[t.Id] is { IsFailure: true, Requirement: null }).ToList();
            if (!retryFailures || !perTest || failing.Count == 0) continue;
            var (retry, retryVerdicts, _) = await RunSuiteAsync(suite, failing, timeout, cancellationToken);
            runs.Add(retry with { Display = retry.Display + Lang.T(" (ulang yang gagal)", " (rerun of the failed ones)") });
            foreach (var test in failing)
                if (retryVerdicts.TryGetValue(test.Id, out var second) && second.Result == UnitTestResult.Passed)
                    verdicts[test.Id] = second with { Flaky = true, Message = verdicts[test.Id].Message };
        }
        return new UnitTestExecution(verdicts, runs, clock.ElapsedMilliseconds);
    }
}
