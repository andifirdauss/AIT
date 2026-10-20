using AiThread.Common.Enums;

namespace AiThread.Service.Orchestration;

public sealed partial class VerificationStage
{
    private static async Task ReportReviewAsync(ReviewResult review, int round, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        if (review.Error is not null)
        {
            await progress.StepAsync($"🟡 Reviewer tidak memberi hasil: {review.Error}", cancellationToken, hang: 3);
            recorder.Log(ActivityLevel.Warning, $"Reviewer putaran {round} gagal: {review.Error}");
            return;
        }

        if (review.Issues.Count == 0)
        {
            await progress.StepAsync($"✅ Reviewer: {review.Summary ?? "tidak ada masalah."}", cancellationToken, hang: 3);
            recorder.Log(ActivityLevel.Success, $"Reviewer putaran {round}: disetujui. {review.Summary}");
            return;
        }

        await progress.StepAsync($"🔎 Reviewer: {review.Issues.Count} masalah{(review.Summary is null ? string.Empty : $" — {review.Summary}")}", cancellationToken, hang: 3);
        foreach (var issue in review.Issues.Take(MaxErrorsInProgress))
            await progress.DetailAsync($"{issue.File} — {issue.Problem}", cancellationToken);
        recorder.Log(ActivityLevel.Warning, $"Reviewer putaran {round}: {review.Issues.Count} masalah. {review.Summary}");
    }
}
