using AIThread.Common.Localization;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    // "14 test: 12 lolos, 1 regresi (UT-0012), 1 sudah gagal sebelumnya", for the thread record and the fallback summary.
    public static string Summary(UnitTestReport report)
    {
        var ran = report.Tests.Where(t => t.After is not null).ToList();
        if (ran.Count == 0) return report.Restored.Count > 0 ? Lang.T($"{report.Restored.Count} test dipulihkan; tidak ada test yang dijalankan.", $"{report.Restored.Count} test(s) restored; no test was run.") : Lang.T("Tidak ada unit test yang dijalankan.", "No unit test was run.");
        var parts = new List<string> { Lang.T($"{ran.Count(t => t.After == "passed")} lolos", $"{ran.Count(t => t.After == "passed")} passed") };
        var regressions = ran.Where(t => t.Verdict == UnitTestReportItem.Regression).Select(t => t.Number).ToList();
        if (regressions.Count > 0) parts.Add(Lang.T($"{regressions.Count} regresi ({string.Join(", ", regressions)})", $"{regressions.Count} regressions ({string.Join(", ", regressions)})"));
        if (ran.Count(t => t.Verdict == UnitTestReportItem.NewFailing) is > 0 and var newFailing) parts.Add(Lang.T($"{newFailing} test baru gagal", $"{newFailing} new test(s) failed"));
        if (ran.Count(t => t.Verdict == UnitTestReportItem.Failing) is > 0 and var failing) parts.Add(Lang.T($"{failing} gagal", $"{failing} failed"));
        if (ran.Count(t => t.Verdict == UnitTestReportItem.Preexisting) is > 0 and var preexisting) parts.Add(Lang.T($"{preexisting} sudah gagal sebelum perubahan", $"{preexisting} already failing before the change"));
        if (ran.Count(t => t.Verdict is UnitTestReportItem.SkippedVerdict or UnitTestReportItem.NotFound or UnitTestReportItem.Missing) is > 0 and var skipped)
            parts.Add(Lang.T($"{skipped} dilewati", $"{skipped} skipped"));
        return $"{ran.Count} test: {string.Join(", ", parts)}.";
    }
}
