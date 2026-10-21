using AIThread.Common.Entities;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class WrapperAgent
{
    // plan and execution include the verification fix tasks; verification is VerificationResult.ForModels and testing
    // TestingResult.ForModels. Node is the wrapper that answered (a fallback when the given one was unavailable).
    public async Task<(string Text, int Tokens, AiNode Node)> WrapAsync(
        AiNode node,
        string userRequest,
        ThreadPlan plan,
        ExecutionSummary execution,
        string? verification,
        Func<string, Task> onDelta,
        CancellationToken cancellationToken,
        Func<NodeSwitch, Task>? onSwitch = null,
        string? testing = null)
    {
        var report = BuildReport(userRequest, plan, execution, verification, testing);
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
