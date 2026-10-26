using System.Text;
using AIThread.Common.Enums;
using AIThread.Common.Localization;

namespace AIThread.Service.Orchestration;

public sealed partial class WrapperAgent
{
    // Used when no wrapper is configured or the wrapper call fails, so the developer still gets the facts.
    public static string FallbackSummary(ThreadPlan plan, ExecutionSummary execution, VerificationReport? verification = null, TestingReport? testing = null,
        UnitTestReport? unitTests = null)
    {
        var builder = new StringBuilder();
        builder.Append(Lang.T($"**Ringkasan:** {plan.Summary}\n\n", $"**Summary:** {plan.Summary}\n\n"));
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
                VerificationReport.Passed => Lang.T("✅ lolos", "✅ passed"),
                VerificationReport.Failed => Lang.T("⚠️ masih ada masalah", "⚠️ issues remain"),
                _ => Lang.T("❔ tidak dapat dipastikan", "❔ could not be determined")
            };
            builder.Append(Lang.T($"\n**Verifikasi:** {outcome}", $"\n**Verification:** {outcome}"));
            if (last.Build is { } build) builder.Append($" — {build.Headline}");
            if (last.Review is { Error: null } review) builder.Append(review.Approved ? Lang.T(" · reviewer setuju", " · reviewer approved") : Lang.T($" · reviewer: {review.Issues.Count} masalah", $" · reviewer: {review.Issues.Count} issues"));
            builder.Append('\n');
            foreach (var error in last.Build?.Errors.Take(10) ?? []) builder.Append($"- `{error}`\n");
            foreach (var issue in last.Review?.Issues ?? []) builder.Append($"- `{issue.File}` — {issue.Problem}\n");
        }

        if (unitTests?.Summary is { } unitSummary) builder.Append($"\n**Unit test:** {unitSummary}\n");

        if (testing is not null)
        {
            builder.Append($"\n**Testing:** {FinalReport.TestingLabel(testing)}");
            if (testing.Summary is not null) builder.Append($" — {testing.Summary}");
            builder.Append('\n');
        }
        return builder.ToString();
    }
}
