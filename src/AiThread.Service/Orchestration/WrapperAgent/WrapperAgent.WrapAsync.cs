using AiThread.Common.Entities;
using AiThread.Service.Models;

namespace AiThread.Service.Orchestration;

public sealed partial class WrapperAgent
{
    // plan and execution include the verification fix tasks; verification is VerificationResult.ForModels.
    // Node is the wrapper that answered (a fallback when the given one was unavailable).
    public async Task<(string Text, int Tokens, AiNode Node)> WrapAsync(
        AiNode node,
        string userRequest,
        ThreadPlan plan,
        ExecutionSummary execution,
        string? verification,
        Func<string, Task> onDelta,
        CancellationToken cancellationToken,
        Func<NodeSwitch, Task>? onSwitch = null)
    {
        var report = BuildReport(userRequest, plan, execution, verification);
        var reply = await caller.ChatAsync(node, async candidate => new ChatCompletionRequest
        {
            Model = candidate.Model,
            Messages =
            [
                ChatMessages.System(await prompts.BuildAsync(AgentPrompts.Wrapper, candidate, cancellationToken)),
                ChatMessages.User(report)
            ],
            Temperature = candidate.Temperature ?? 0.3,
            MaxTokens = candidate.MaxTokens
        }, cancellationToken, onDelta, onSwitch);

        return (reply.Result.Content, reply.Result.TotalTokens, reply.Node);
    }
}
