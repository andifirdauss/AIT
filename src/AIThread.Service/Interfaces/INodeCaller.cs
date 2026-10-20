using AIThread.Common.Entities;
using AIThread.Service.Models;
using AIThread.Service.Orchestration;

namespace AIThread.Service.Interfaces;

public interface INodeCaller
{
    Task<NodeReply> ChatAsync(AiNode node, Func<AiNode, Task<ChatCompletionRequest>> build, CancellationToken cancellationToken, Func<string, Task>? onDelta = null, Func<NodeSwitch, Task>? onSwitch = null, bool slotHeld = false);
}
