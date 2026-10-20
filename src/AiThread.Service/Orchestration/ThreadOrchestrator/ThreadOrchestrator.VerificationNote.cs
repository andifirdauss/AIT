namespace AiThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    private static string VerificationNote(VerificationReport report)
    {
        var last = report.Rounds.LastOrDefault();
        var parts = new List<string>();
        if (last?.Build is { Status: "failed" } build) parts.Add($"build masih gagal ({build.ErrorCount} error)");
        if (last?.Review is { Error: null, Issues.Count: > 0 } review) parts.Add($"reviewer masih menemukan {review.Issues.Count} masalah");
        return parts.Count == 0 ? "Verifikasi belum lolos." : $"Verifikasi: {string.Join(", ", parts)}.";
    }
}
