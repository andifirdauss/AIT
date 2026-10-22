using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.UnitTests;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    // The catalog's last result when the test file and its targets are exactly as they were then (same hash): the same
    // code gives the same result, whoever ran it. Skips are not reused: the environment may have changed.
    private static TestVerdict? Cached(UnitTestSession session, UnitTestCase test)
    {
        if (test.LastRunAt is not { } at || DateTime.UtcNow - at > CacheAge) return null;
        if (test.LastResult is not (UnitTestResult.Passed or UnitTestResult.Failed or UnitTestResult.Error)) return null;
        if (test.LastInputHash is null || test.LastInputHash != UnitTestText.InputHash(session.Project.RootPath, test)) return null;
        return new TestVerdict(test.LastResult, test.LastDurationMs, test.LastMessage, null);
    }
}
