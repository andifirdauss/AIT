using AIThread.Common.Enums;
using AIThread.Common.Localization;

namespace AIThread.Service.Orchestration;

public sealed partial class VerificationStage
{
    private static async Task ReportReviewAsync(ReviewResult review, int round, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        if (review.Error is not null)
        {
            await progress.StepAsync(Lang.T($"🟡 Reviewer tidak memberi hasil: {review.Error}", $"🟡 The reviewer gave no result: {review.Error}"), cancellationToken, hang: 3);
            recorder.Log(ActivityLevel.Warning, Lang.T($"Reviewer putaran {round} gagal: {review.Error}", $"Reviewer round {round} failed: {review.Error}"));
            return;
        }

        if (review.Issues.Count == 0)
        {
            await progress.StepAsync(Lang.T($"✅ Reviewer: {review.Summary ?? "tidak ada masalah."}", $"✅ Reviewer: {review.Summary ?? "no issues."}"), cancellationToken, hang: 3);
            recorder.Log(ActivityLevel.Success, Lang.T($"Reviewer putaran {round}: disetujui. {review.Summary}", $"Reviewer round {round}: approved. {review.Summary}"));
            return;
        }

        await progress.StepAsync(Lang.T($"🔎 Reviewer: {review.Issues.Count} masalah{(review.Summary is null ? string.Empty : $" — {review.Summary}")}", $"🔎 Reviewer: {review.Issues.Count} issues{(review.Summary is null ? string.Empty : $" — {review.Summary}")}"), cancellationToken, hang: 3);
        foreach (var issue in review.Issues.Take(MaxErrorsInProgress))
            await progress.DetailAsync($"{issue.File} — {issue.Problem}", cancellationToken);
        recorder.Log(ActivityLevel.Warning, Lang.T($"Reviewer putaran {round}: {review.Issues.Count} masalah. {review.Summary}", $"Reviewer round {round}: {review.Issues.Count} issues. {review.Summary}"));
    }
}
