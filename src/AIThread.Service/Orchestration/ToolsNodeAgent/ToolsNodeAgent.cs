using AIThread.Common.Entities;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Orchestration;

// The tools a planner delegated to one tools node. Key is how the planner names it in investigate.
public sealed record ToolsNodeGroup(string Key, AiNode Node, IReadOnlyList<string> Tools);

// Elapsed: the whole investigation; ModelTime: the tools node answering, ReportTime of it the last turn that wrote the
// findings; QueueTime: the wait for a tools node slot.
public sealed record ToolsNodeInvestigation(string Findings, int Steps, int Tokens, int CachedTokens, AiNode Node,
    TimeSpan Elapsed = default, TimeSpan ModelTime = default, TimeSpan QueueTime = default, TimeSpan ReportTime = default);

// A cheap model that answers one planner question with the planner's delegated tools, in its own tool-calling loop,
// and returns only its findings: the raw tool output stays in this conversation, which ends with the call.
public sealed partial class ToolsNodeAgent(INodeCaller caller, IPlannerToolRunner runner, INodePromptBuilder prompts) : IToolsNodeAgent
{
    private const int MaxSteps = 12;
    // The prompt asks for far less; writing a long report is the slowest turn of a cheap model.
    private const int MaxFindingsChars = 6_000;
    private const int MapDepth = 4;
    private const int MaxMapEntries = 300;
}
