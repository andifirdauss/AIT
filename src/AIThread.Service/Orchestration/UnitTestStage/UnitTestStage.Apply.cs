using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.UnitTests;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    // The catalog's last result of a test. inputHash: of the code the result belongs to, null when that is not known.
    public static void Apply(UnitTestCase test, TestVerdict verdict, string? inputHash, DateTime now, string by)
    {
        test.LastResult = verdict.Result;
        test.LastRunAt = now;
        test.LastRunBy = by;
        test.LastDurationMs = verdict.DurationMs;
        test.LastMessage = verdict.Message is null ? null : UnitTestText.Clip(verdict.Message, 2_000);
        test.LastSkipReason = verdict.Result == UnitTestResult.Skipped ? UnitTestText.Clip(verdict.SkipReason ?? "Dilewati tanpa alasan.", 500) : null;
        test.LastInputHash = inputHash;
        if (verdict.Requirement is { } requirement) test.Requires = UnitTestText.JoinCsv(UnitTestText.Lines(test.Requires).Append(requirement));
    }
}
