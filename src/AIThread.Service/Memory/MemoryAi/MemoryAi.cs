using AIThread.Common.Enums;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Memory;

public sealed class MemoryAiUnavailableException() : Exception("No active Tools node, Wrapper or Planner to label memory with.");

// The model calls of the memory catalog (code map, relabeling, duplicate modules): the first active tools node (cheap),
// else the wrapper, else the planner, each with its own fallback chain and concurrency limit.
public sealed partial class MemoryAi(INodeCaller caller, INodeService nodeService) : IMemoryAi
{
    private static readonly NodeRole[] Roles = [NodeRole.ToolsNode, NodeRole.Wrapper, NodeRole.Planner];
}
