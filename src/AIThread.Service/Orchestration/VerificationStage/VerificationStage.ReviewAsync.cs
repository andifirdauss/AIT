using AIThread.Common.Entities;
using AIThread.Service.Models;
using AIThread.Service.Verification;

namespace AIThread.Service.Orchestration;

public sealed partial class VerificationStage
{
    // A reviewer that cannot be reached or answers unreadably only costs the review, never the thread.
    private async Task<ReviewResult> ReviewAsync(
        AiNode reviewer, VerificationInput input, IReadOnlyList<PlanTask> tasks, IReadOnlyList<TaskResult> results, BuildResult? build,
        ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        try
        {
            var buildText = build is null ? null : BuildReport.Format(build, input.Scope);
            return await reviewerAgent.ReviewAsync(reviewer, input.Request.Prompt, input.Plan, tasks, results, buildText, cancellationToken,
                s => s.ReportAsync("Reviewer", recorder, progress, cancellationToken));
        }
        catch (NineRouterException ex)
        {
            return ReviewResult.Failed(ex.Message);
        }
    }
}
