namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    // "14 test: 12 lolos, 1 regresi (UT-0012), 1 sudah gagal sebelumnya", for the thread record and the fallback summary.
    public static string Summary(UnitTestReport report)
    {
        var ran = report.Tests.Where(t => t.After is not null).ToList();
        if (ran.Count == 0) return report.Restored.Count > 0 ? $"{report.Restored.Count} test dipulihkan; tidak ada test yang dijalankan." : "Tidak ada unit test yang dijalankan.";
        var parts = new List<string> { $"{ran.Count(t => t.After == "passed")} lolos" };
        var regressions = ran.Where(t => t.Verdict == UnitTestReportItem.Regression).Select(t => t.Number).ToList();
        if (regressions.Count > 0) parts.Add($"{regressions.Count} regresi ({string.Join(", ", regressions)})");
        if (ran.Count(t => t.Verdict == UnitTestReportItem.NewFailing) is > 0 and var newFailing) parts.Add($"{newFailing} test baru gagal");
        if (ran.Count(t => t.Verdict == UnitTestReportItem.Failing) is > 0 and var failing) parts.Add($"{failing} gagal");
        if (ran.Count(t => t.Verdict == UnitTestReportItem.Preexisting) is > 0 and var preexisting) parts.Add($"{preexisting} sudah gagal sebelum perubahan");
        if (ran.Count(t => t.Verdict is UnitTestReportItem.SkippedVerdict or UnitTestReportItem.NotFound or UnitTestReportItem.Missing) is > 0 and var skipped)
            parts.Add($"{skipped} dilewati");
        return $"{ran.Count} test: {string.Join(", ", parts)}.";
    }
}
