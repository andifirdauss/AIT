using AiThread.Common.Entities;
using AiThread.Service.Interfaces;

namespace AiThread.Service.Orchestration;

// The tools a planner delegated to one tools node. Key is how the planner names it in investigate.
public sealed record ToolsNodeGroup(string Key, AiNode Node, IReadOnlyList<string> Tools);

public sealed record ToolsNodeInvestigation(string Findings, int Steps, int Tokens, int CachedTokens, AiNode Node);

// A cheap model that answers one planner question with the planner's delegated tools, in its own tool-calling loop,
// and returns only its findings: the raw tool output stays in this conversation, which ends with the call.
public sealed partial class ToolsNodeAgent(INodeCaller caller, IPlannerToolRunner runner, INodePromptBuilder prompts) : IToolsNodeAgent
{
    private const int MaxSteps = 12;
    private const int MaxFindingsChars = 12_000;
}
