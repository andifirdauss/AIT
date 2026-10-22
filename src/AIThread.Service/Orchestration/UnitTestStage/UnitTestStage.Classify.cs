using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.UnitTests;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    // What a result after the change means. A failure is a regression when the test passed before (the baseline, else
    // the catalog's last result); a test that failed before too is only reported. An updated test must pass, a new one too.
    private static string Classify(UnitTestCase test, TestVerdict? baseline, TestVerdict after, bool isNew, string decision)
    {
        switch (after.Result)
        {
            case UnitTestResult.Passed:
                return baseline?.IsFailure == true ? UnitTestReportItem.Fixed : after.Flaky ? UnitTestReportItem.Flaky : UnitTestReportItem.Ok;
            case UnitTestResult.Skipped:
            case UnitTestResult.Unknown:
                return UnitTestReportItem.SkippedVerdict;
            case UnitTestResult.NotFound:
                return isNew ? UnitTestReportItem.Missing : UnitTestReportItem.NotFound;
        }

        if (isNew) return UnitTestReportItem.NewFailing;
        if (decision == PlanTests.Update) return UnitTestReportItem.Regression;
        var before = baseline is { Result: UnitTestResult.Passed or UnitTestResult.Failed or UnitTestResult.Error } ? baseline.Result : test.LastResult;
        return before is UnitTestResult.Failed or UnitTestResult.Error ? UnitTestReportItem.Preexisting : UnitTestReportItem.Regression;
    }
}
