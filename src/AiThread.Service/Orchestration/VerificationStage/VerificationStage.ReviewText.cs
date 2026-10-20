using System.Text;

namespace AiThread.Service.Orchestration;

public sealed partial class VerificationStage
{
    private static string ReviewText(ReviewResult review)
    {
        if (review.Error is not null) return $"Reviewer: no result ({review.Error}).\n";
        if (review.Issues.Count == 0) return $"Reviewer: approved. {review.Summary}\n";

        var builder = new StringBuilder($"Reviewer: {review.Issues.Count} issue(s). {review.Summary}\n");
        foreach (var issue in review.Issues) builder.Append($"- {issue.File}: {issue.Problem} → {issue.Fix}\n");
        return builder.ToString();
    }
}
