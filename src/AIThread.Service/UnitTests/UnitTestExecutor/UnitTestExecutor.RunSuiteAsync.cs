using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Verification;

namespace AIThread.Service.UnitTests;

public sealed partial class UnitTestExecutor
{
    // PerTest: the verdicts come from the runner's per-test report, not from the outcome of the whole run.
    private async Task<(SuiteRun Run, Dictionary<Guid, TestVerdict> Verdicts, bool PerTest)> RunSuiteAsync(TestSuite suite, IReadOnlyList<UnitTestCase> tests,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        var targets = tests.Select(t => new TestTarget(t.Selector, t.TestFile, t.ClassName, t.MethodName)).ToList();
        var result = await runner.RunAsync(suite, suite.PerTestResults ? targets : null, timeout, cancellationToken);
        var run = new SuiteRun(result.Command.Display, result.Succeeded, result.ExitCode, result.TimedOut, result.StartError ?? result.ReportError,
            result.OutputTail, result.DurationMs, tests.Count);

        var verdicts = new Dictionary<Guid, TestVerdict>();
        if (result.Outcomes.Count > 0)
        {
            foreach (var test in tests)
                verdicts[test.Id] = UnitTestMatcher.Match(test, suite, result.Outcomes)
                                    ?? new TestVerdict(UnitTestResult.NotFound, null, Lang.T("Runner tidak melaporkan test ini; namanya atau file-nya mungkin sudah berubah.", "The runner did not report this test; its name or file may have changed."), null);
            return (run, verdicts, true);
        }

        var whole = WholeRun(suite, result);
        foreach (var test in tests) verdicts[test.Id] = whole;
        return (run, verdicts, false);
    }
}
