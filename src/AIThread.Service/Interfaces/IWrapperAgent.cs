using AIThread.Common.Entities;
using AIThread.Service.Orchestration;

namespace AIThread.Service.Interfaces;

public interface IWrapperAgent
{
    Task<(string Text, int Tokens, AiNode Node)> WrapAsync(AiNode node, string userRequest, ThreadPlan plan, ExecutionSummary execution, string? verification, Func<string, Task> onDelta, CancellationToken cancellationToken, Func<NodeSwitch, Task>? onSwitch = null);
}
