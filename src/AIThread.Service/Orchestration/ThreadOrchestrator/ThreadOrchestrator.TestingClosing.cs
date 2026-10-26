using AIThread.Common.Localization;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // Closes the testing phase: "lolos 2/2 skenario", "1 dari 3 skenario gagal".
    private static string TestingClosing(TestingReport report)
    {
        var total = report.Scenarios.Count;
        var blocked = report.Count(TestingReport.Blocked);
        if (report.LoginNeeded is not null) return report.AskedLogin ? Lang.T("menunggu login dari developer", "waiting for a login from the developer") : Lang.T("butuh login (lihat ringkasan)", "needs a login (see the summary)");
        return report.Outcome switch
        {
            TestingReport.Passed => blocked == 0
                ? Lang.T($"lolos {report.Count(TestingReport.Passed)}/{total} skenario", $"passed {report.Count(TestingReport.Passed)}/{total} scenarios")
                : Lang.T($"{report.Count(TestingReport.Passed)}/{total} skenario lolos, {blocked} tidak bisa dicek", $"{report.Count(TestingReport.Passed)}/{total} scenarios passed, {blocked} could not be checked"),
            TestingReport.Failed => Lang.T($"{report.Count(TestingReport.Failed)} dari {total} skenario gagal (lihat ringkasan)", $"{report.Count(TestingReport.Failed)} of {total} scenarios failed (see the summary)"),
            _ => Lang.T("tidak ada skenario yang bisa dicek (lihat ringkasan)", "no scenario could be checked (see the summary)")
        };
    }
}
