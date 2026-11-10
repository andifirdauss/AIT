using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Service.Orchestration;

namespace AIThread.Service.Implementations;

public sealed partial class NodeBenchmarkService
{
    public async Task<NodeBenchmarkDto> RunAsync(CancellationToken cancellationToken = default)
    {
        var planner = (await nodes.GetActiveAsync(NodeRole.Planner, cancellationToken)).FirstOrDefault();
        var executor = (await nodes.GetActiveAsync(NodeRole.Executor, cancellationToken)).FirstOrDefault();
        var reviewer = (await nodes.GetActiveAsync(NodeRole.Reviewer, cancellationToken)).FirstOrDefault();
        var wrapper = (await nodes.GetActiveAsync(NodeRole.Wrapper, cancellationToken)).FirstOrDefault();
        var toolsNode = await ToolsNodeAsync(planner, cancellationToken);

        var steps = NodeBenchmarkReport.Steps(planner, toolsNode, executor, reviewer, wrapper, options.Value.WrapperSkipMaxTasks);
        var measured = await Task.WhenAll(steps.Select(s => s.Node.Model).Distinct(StringComparer.Ordinal).Select(m => MeasureAsync(m, cancellationToken)));
        return NodeBenchmarkReport.Build(steps, measured, Samples);
    }
}
