using AIThread.Common.Enums;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    // "passed", "failed", ... as the report stores results.
    private static string Lower(UnitTestResult result) => result.ToString().ToLowerInvariant();
}
