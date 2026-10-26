using AIThread.Common.Localization;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    private static string VerificationNote(VerificationReport report)
    {
        var last = report.Rounds.LastOrDefault();
        var parts = new List<string>();
        if (last?.Build is { Status: "failed" } build) parts.Add(Lang.T($"build masih gagal ({build.ErrorCount} error)", $"the build still fails ({build.ErrorCount} errors)"));
        if (last?.Review is { Error: null, Issues.Count: > 0 } review) parts.Add(Lang.T($"reviewer masih menemukan {review.Issues.Count} masalah", $"the reviewer still finds {review.Issues.Count} issue(s)"));
        return parts.Count == 0 ? Lang.T("Verifikasi belum lolos.", "Verification not passed yet.") : Lang.T($"Verifikasi: {string.Join(", ", parts)}.", $"Verification: {string.Join(", ", parts)}.");
    }
}
