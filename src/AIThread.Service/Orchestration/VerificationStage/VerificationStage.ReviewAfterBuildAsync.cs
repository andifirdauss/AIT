using AIThread.Common.Entities;
using AIThread.Service.Verification;

namespace AIThread.Service.Orchestration;

public sealed partial class VerificationStage
{
    // The review started next to the build counts when the build passed. A build that failed has errors the reviewer
    // must see to name their cause, so that review is dropped and a new one reads them.
    private async Task<ReviewResult> ReviewAfterBuildAsync(
        Task<ReviewResult>? earlyReview, CancellationTokenSource earlyReviewStop, AiNode reviewer, VerificationInput input, IReadOnlyList<PlanTask> tasks,
        IReadOnlyList<TaskResult> results, BuildResult? build, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        if (earlyReview is not null)
        {
            if (build is { Passed: true }) return await earlyReview;
            await earlyReviewStop.CancelAsync();
            try
            {
                await earlyReview;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Dropped on purpose.
            }
        }
        return await ReviewAsync(reviewer, input, tasks, results, build, recorder, progress, cancellationToken);
    }
}
