using AIThread.Common.Entities;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class ReviewerAgent
{
    public async Task<ReviewResult> ReviewAsync(
        AiNode node,
        string userRequest,
        ThreadPlan plan,
        IReadOnlyList<PlanTask> tasks,
        IReadOnlyList<TaskResult> results,
        string? buildReport,
        CancellationToken cancellationToken,
        Func<NodeSwitch, Task>? onSwitch = null)
    {
        var input = BuildInput(userRequest, plan, tasks, results, buildReport);
        var reply = await caller.ChatAsync(node, async candidate => new ChatCompletionRequest
        {
            Model = candidate.Model,
            Messages =
            [
                ChatMessages.System(await prompts.BuildAsync(AgentPrompts.Reviewer, candidate, cancellationToken)),
                ChatMessages.User(input)
            ],
            Temperature = candidate.Temperature ?? 0.1,
            MaxTokens = candidate.MaxTokens
        }, cancellationToken, onSwitch: onSwitch);

        return Parse(reply.Result.Content) with { Tokens = reply.Result.TotalTokens };
    }
}
