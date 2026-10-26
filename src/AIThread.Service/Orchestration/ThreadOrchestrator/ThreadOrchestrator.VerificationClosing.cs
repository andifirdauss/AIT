using AIThread.Common.Localization;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // Closes the verification phase: "lolos setelah 1 putaran perbaikan".
    private static string VerificationClosing(VerificationReport report)
    {
        var fixRounds = report.Rounds.Count(r => r.FixTasks.Count > 0);
        return report.Outcome switch
        {
            VerificationReport.Passed => fixRounds == 0 ? Lang.T("lolos", "passed") : Lang.T($"lolos setelah {fixRounds} putaran perbaikan", $"passed after {fixRounds} fix round(s)"),
            VerificationReport.Failed => Lang.T("belum lolos, masih ada masalah (lihat ringkasan)", "not passed yet, issues remain (see the summary)"),
            _ => Lang.T("hasil tidak dapat dipastikan", "result could not be determined")
        };
    }
}
