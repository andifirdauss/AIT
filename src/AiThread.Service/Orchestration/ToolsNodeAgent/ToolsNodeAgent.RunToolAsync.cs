using AiThread.Service.Models;
using AiThread.Service.Workspace;

namespace AiThread.Service.Orchestration;

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
        return await runner.RunAsync(call.Name, args, input, tools, memoriesRead, prefix, recorder, progress, cancellationToken);
    }
}
