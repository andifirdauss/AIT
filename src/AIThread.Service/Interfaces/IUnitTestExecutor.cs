using AIThread.Common.Entities;
using AIThread.Service.UnitTests;
using AIThread.Service.Verification;

namespace AIThread.Service.Interfaces;

public interface IUnitTestExecutor
{
    // Runs the tests suite by suite and gives a verdict for every one of them (a test that cannot run is Skipped with
    // the reason). retryFailures: a failing test runs once more; passing then marks it Flaky.
    Task<UnitTestExecution> RunAsync(string root, IReadOnlyList<TestSuite> suites, IReadOnlyList<UnitTestCase> tests, TimeSpan timeout,
        bool retryFailures, CancellationToken cancellationToken);
}
