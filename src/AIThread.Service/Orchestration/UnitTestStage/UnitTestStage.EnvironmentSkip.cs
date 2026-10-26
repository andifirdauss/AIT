using AIThread.Common.Localization;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.UnitTests;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    // A test that needs something this machine lacked the last time (Requires, recorded from that skip) is skipped again
    // with the same reason instead of waiting for a connection timeout. Running it from Project > Detail tries again.
    private static TestVerdict? EnvironmentSkip(UnitTestSession session, UnitTestCase test)
    {
        if (string.IsNullOrEmpty(test.Requires)) return null;
        if (!session.MachineRuns.TryGetValue(test.Id, out var run) || run.Result != UnitTestResult.Skipped || run.SkipReason is null) return null;
        if (DateTime.UtcNow - run.CreatedAt > EnvironmentSkipAge) return null;
        var again = Lang.T(" (tidak dicoba lagi: run terakhir di mesin ini juga dilewati)", " (not tried again: the last run on this machine was skipped too)");
        var reason = run.SkipReason.EndsWith(again, StringComparison.Ordinal) ? run.SkipReason : run.SkipReason + again;
        return new TestVerdict(UnitTestResult.Skipped, null, null, reason, UnitTestText.Lines(test.Requires).FirstOrDefault(), Remembered: true);
    }
}
