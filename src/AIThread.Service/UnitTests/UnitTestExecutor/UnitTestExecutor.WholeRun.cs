using AIThread.Common.Enums;
using AIThread.Service.Verification;

namespace AIThread.Service.UnitTests;

public sealed partial class UnitTestExecutor
{
    // The verdict of every test when the runner gave no result per test.
    private static TestVerdict WholeRun(TestSuite suite, TestRunResult result)
    {
        var tail = TestResultParser.Clip(Tail(result.OutputTail));
        if (result.StartError is not null) return new TestVerdict(UnitTestResult.Skipped, null, null, result.StartError);
        if (result.TimedOut) return new TestVerdict(UnitTestResult.Error, result.DurationMs, $"Run test melewati batas waktu ({result.DurationMs / 1000} detik) dan dihentikan.\n{tail}", null);
        if (TestEnvironment.Classify(result.OutputTail) is { } environment && result.ExitCode != 0)
            return new TestVerdict(UnitTestResult.Skipped, result.DurationMs, tail, environment.Reason, environment.Requirement);

        // Without per-test results (npm test with an unknown runner) the suite passes or fails as a whole.
        if (suite.Format == TestReportFormat.ExitCode)
            return result.ExitCode == 0
                ? new TestVerdict(UnitTestResult.Passed, result.DurationMs, null, null)
                : new TestVerdict(UnitTestResult.Failed, result.DurationMs, $"`{result.Command.Display}` gagal (exit code {result.ExitCode}); runner ini tidak memberi hasil per test.\n{tail}", null);

        return result.ExitCode == 0
            ? new TestVerdict(UnitTestResult.Skipped, result.DurationMs, null, result.ReportError ?? "Runner tidak memberi hasil per test.")
            // Usually the test project no longer compiles: that is a failure of every test in it.
            : new TestVerdict(UnitTestResult.Error, result.DurationMs, $"Test tidak bisa dijalankan (exit code {result.ExitCode}): {result.ReportError}\n{tail}", null);
    }
}
