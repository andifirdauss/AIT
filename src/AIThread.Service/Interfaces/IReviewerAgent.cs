using AIThread.Common.Entities;
using AIThread.Service.Orchestration;

namespace AIThread.Service.Interfaces;

public interface IReviewerAgent
{
    Task<ReviewResult> ReviewAsync(AiNode node, string userRequest, ThreadPlan plan, IReadOnlyList<PlanTask> tasks, IReadOnlyList<TaskResult> results, string? buildReport, CancellationToken cancellationToken, Func<NodeSwitch, Task>? onSwitch = null);
}
