namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // Closes the testing phase: "lolos 2/2 skenario", "1 dari 3 skenario gagal".
    private static string TestingClosing(TestingReport report)
    {
        var total = report.Scenarios.Count;
        var blocked = report.Count(TestingReport.Blocked);
        if (report.LoginNeeded is not null) return report.AskedLogin ? "menunggu login dari developer" : "butuh login (lihat ringkasan)";
        return report.Outcome switch
        {
            TestingReport.Passed => blocked == 0
                ? $"lolos {report.Count(TestingReport.Passed)}/{total} skenario"
                : $"{report.Count(TestingReport.Passed)}/{total} skenario lolos, {blocked} tidak bisa dicek",
            TestingReport.Failed => $"{report.Count(TestingReport.Failed)} dari {total} skenario gagal (lihat ringkasan)",
            _ => "tidak ada skenario yang bisa dicek (lihat ringkasan)"
        };
    }
}
