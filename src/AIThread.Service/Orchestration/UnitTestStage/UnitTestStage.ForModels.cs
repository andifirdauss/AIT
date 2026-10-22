using System.Text;
using AIThread.Common.Entities;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    // The round as the wrapper reads it (English, inside the verification text).
    private static string ForModels(UnitTestSession session, IReadOnlyList<UnitTestCase> tests, UnitTestRoundReport round)
    {
        var builder = new StringBuilder($"Unit tests after the change (round {round.Round}): {tests.Count} tests, {round.Passed} passed, {round.Failed} failing, {round.Skipped} skipped.\n");
        foreach (var item in tests.Select(t => session.Items[t.Id]).Where(i => i.Verdict is not (UnitTestReportItem.Ok or UnitTestReportItem.Fixed)))
        {
            var what = item.Verdict switch
            {
                UnitTestReportItem.Regression => "REGRESSION (passed before, fails now)",
                UnitTestReportItem.NewFailing => "new test of this change fails",
                UnitTestReportItem.Preexisting => "already failed before the change (not caused by it)",
                UnitTestReportItem.Missing => "new test not found in its file",
                UnitTestReportItem.NotFound => "not reported by the runner",
                UnitTestReportItem.Flaky => "unstable (passed on retry or judged flaky)",
                _ => "skipped"
            };
            builder.Append($"- {item.Number} \"{item.Title}\": {what}");
            if (item.SkipReason is not null) builder.Append($" — {item.SkipReason}");
            else if (item.Message is not null) builder.Append($" — {OneLine(item.Message)}");
            if (item.Triage is not null) builder.Append($" [tester: {item.Triage}{(item.TriageReason is null ? string.Empty : $", {item.TriageReason}")}]");
            builder.Append('\n');
        }
        if (round.Triage is not null) builder.Append($"Tester's triage: {round.Triage}\n");
        if (round.FixTargets.Count > 0) builder.Append($"Fix tasks target: {string.Join(", ", round.FixTargets)}\n");
        return builder.ToString();
    }
}
