using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.UnitTests;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    // "🧪 Unit test · 14 test · 12 lolos, 1 regresi, 1 sudah gagal sebelumnya (8.2s)" and a line per problem.
    private static async Task ReportRoundAsync(UnitTestSession session, IReadOnlyList<UnitTestCase> tests, UnitTestRoundReport round, ThreadRecorder recorder,
        ClientProgress progress, CancellationToken cancellationToken)
    {
        var items = tests.Select(t => session.Items[t.Id]).ToList();
        var problems = items.Where(i => i.IsProblem).ToList();
        var parts = new List<string> { Lang.T($"{round.Passed} lolos", $"{round.Passed} passed") };
        if (items.Count(i => i.Verdict == UnitTestReportItem.Regression) is > 0 and var regressions) parts.Add(Lang.T($"{regressions} regresi", $"{regressions} regressions"));
        if (items.Count(i => i.Verdict == UnitTestReportItem.NewFailing) is > 0 and var newFailing) parts.Add(Lang.T($"{newFailing} test baru gagal", $"{newFailing} new test(s) failed"));
        if (round.Preexisting.Count > 0) parts.Add(Lang.T($"{round.Preexisting.Count} sudah gagal sebelum perubahan", $"{round.Preexisting.Count} already failing before the change"));
        if (items.Count(i => i.Verdict is UnitTestReportItem.SkippedVerdict or UnitTestReportItem.NotFound or UnitTestReportItem.Missing) is > 0 and var skipped)
            parts.Add(Lang.T($"{skipped} dilewati", $"{skipped} skipped"));
        if (items.Count(i => i.Verdict == UnitTestReportItem.Flaky) is > 0 and var flaky) parts.Add(Lang.T($"{flaky} tidak stabil", $"{flaky} flaky"));
        var icon = problems.Count > 0 ? "🔴" : round.Failed > 0 || round.Preexisting.Count > 0 ? "🟡" : "✅";
        await progress.StepAsync($"{icon} Unit test · {tests.Count} test · {string.Join(", ", parts)} ({round.DurationMs / 1000.0:0.0}s)", cancellationToken, hang: 3);
        foreach (var item in problems.Take(MaxProgressFailures))
            await progress.DetailAsync($"{item.Number} {item.Title} — {OneLine(item.Message)}", cancellationToken);
        recorder.Log(problems.Count > 0 ? ActivityLevel.Warning : ActivityLevel.Success,
            Lang.T($"Unit test putaran {round.Round}: {tests.Count} test, {string.Join(", ", parts)}", $"Unit test round {round.Round}: {tests.Count} tests, {string.Join(", ", parts)}") + (problems.Count > 0 ? $" ({string.Join(", ", problems.Select(p => p.Number))})" : string.Empty) + ".");
    }
}
