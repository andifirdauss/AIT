using AIThread.Service.Interfaces;

namespace AIThread.Service.UnitTests;

// One run of one suite as the developer and the models see it.
public sealed record SuiteRun(string Display, bool Succeeded, int? ExitCode, bool TimedOut, string? Error, string OutputTail, long DurationMs, int Tests);

public sealed record UnitTestExecution(IReadOnlyDictionary<Guid, TestVerdict> Verdicts, IReadOnlyList<SuiteRun> Suites, long DurationMs);

// Groups catalog tests by the suite their file belongs to, runs each suite once with a filter of just those tests
// (ITestRunner), and turns what the runner reported into one verdict per test.
public sealed partial class UnitTestExecutor(ITestRunner runner) : IUnitTestExecutor
{
    // Output lines of a failed suite run kept as the message of its tests when no per-test result exists.
    private const int MaxSuiteMessageChars = 1_500;
}
