using AIThread.Service.Verification;

namespace AIThread.Service.Interfaces;

public interface ITestRunner
{
    // Runs a suite, or only the targets of it, and reads its report. Shares the per-root gate with builds.
    Task<TestRunResult> RunAsync(TestSuite suite, IReadOnlyList<TestTarget>? targets, TimeSpan timeout, CancellationToken cancellationToken);
}
