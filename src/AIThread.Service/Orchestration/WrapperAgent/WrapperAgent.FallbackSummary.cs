using System.Text;
using AIThread.Common.Enums;

namespace AIThread.Service.Orchestration;

public sealed partial class WrapperAgent
{
    // Used when no wrapper is configured or the wrapper call fails, so the developer still gets the facts.
    public static string FallbackSummary(ThreadPlan plan, ExecutionSummary execution, VerificationReport? verification = null)
    {
        var builder = new StringBuilder();
        builder.Append($"**Ringkasan:** {plan.Summary}\n\n");
        foreach (var result in execution.Results)
        {
            var icon = result.Status switch
            {
                ThreadTaskStatus.Succeeded => "✅",
                ThreadTaskStatus.Skipped => "⏭️",
                _ => "❌"
            };
            builder.Append($"- {icon} **{result.TaskKey}** `{result.File}` — {(result.Status == ThreadTaskStatus.Succeeded ? result.Summary : result.Error)}\n");
        }

        if (verification?.Rounds.LastOrDefault() is { } last)
        {
            var outcome = verification.Outcome switch
            {
                VerificationReport.Passed => "✅ lolos",
                VerificationReport.Failed => "⚠️ masih ada masalah",
                _ => "❔ tidak dapat dipastikan"
            };
            builder.Append($"\n**Verifikasi:** {outcome}");
            if (last.Build is { } build) builder.Append($" — {build.Headline}");
            if (last.Review is { Error: null } review) builder.Append(review.Approved ? " · reviewer setuju" : $" · reviewer: {review.Issues.Count} masalah");
            builder.Append('\n');
            foreach (var error in last.Build?.Errors.Take(10) ?? []) builder.Append($"- `{error}`\n");
            foreach (var issue in last.Review?.Issues ?? []) builder.Append($"- `{issue.File}` — {issue.Problem}\n");
        }
        return builder.ToString();
    }
}
