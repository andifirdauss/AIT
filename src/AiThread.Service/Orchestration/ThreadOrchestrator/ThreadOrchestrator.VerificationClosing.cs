namespace AiThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // Closes the verification phase: "lolos setelah 1 putaran perbaikan".
    private static string VerificationClosing(VerificationReport report)
    {
        var fixRounds = report.Rounds.Count(r => r.FixTasks.Count > 0);
        return report.Outcome switch
        {
            VerificationReport.Passed => fixRounds == 0 ? "lolos" : $"lolos setelah {fixRounds} putaran perbaikan",
            VerificationReport.Failed => "belum lolos, masih ada masalah (lihat ringkasan)",
            _ => "hasil tidak dapat dipastikan"
        };
    }
}
