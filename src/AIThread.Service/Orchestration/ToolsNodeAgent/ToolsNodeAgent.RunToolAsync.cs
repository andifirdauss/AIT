using AIThread.Service.Models;
using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

public sealed partial class ToolsNodeAgent
{
    private async Task<PlannerToolOutcome> RunToolAsync(
        ToolCall call,
        ToolsNodeGroup group,
        PlannerInput input,
        WorkspaceTools tools,
        HashSet<Guid> memoriesRead,
        string prefix,
        string actor,
        ThreadRecorder recorder,
        ClientProgress progress,
        string? badge,
        CancellationToken cancellationToken)
    {
        var available = PlannerTools.ToolsNodeTools(group.Tools);
        if (!available.Contains(call.Name))
            return new PlannerToolOutcome($"Error: tool '{call.Name}' is not available. Available: {string.Join(", ", available)}, {PlannerTools.ReportFindings}.", null, true);

        var args = PlannerToolRunner.ParseArguments(call.Arguments, out var error);
        if (args is null) return error!;

        await PlannerToolRunner.AnnounceAsync(call.Name, args, actor, prefix, recorder, progress, cancellationToken, badge);
        // A tools node cannot pause the thread for the developer's permission: such a call fails with a note for the planner.
        return await runner.RunAsync(call.Name, args, input with { CanApprove = false }, tools, memoriesRead, prefix, recorder, progress, cancellationToken);
    }
}
