using AIThread.Service.Models;
using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    private async Task<PlannerToolOutcome> RunToolAsync(
        ToolCall call,
        PlannerInput input,
        PlannerRun run,
        WorkspaceTools tools,
        ThreadRecorder recorder,
        ClientProgress progress,
        string? badge,
        CancellationToken cancellationToken)
    {
        var args = PlannerToolRunner.ParseArguments(call.Arguments, out var error);
        if (args is null) return error!;

        // A delegated tool is not in the planner's list; a model that calls it anyway is pointed to investigate.
        if (run.Groups.FirstOrDefault(g => g.Tools.Contains(call.Name)) is { } holder)
            return new PlannerToolOutcome($"Error: {call.Name} is delegated to a sub agent{(run.Groups.Count > 1 ? $" (\"{holder.Key}\")" : string.Empty)}. Call investigate with your question instead.", null, true);

        // investigate numbers its own line; submit_plan has none.
        if (call.Name is not (PlannerTools.SubmitPlan or PlannerTools.Investigate))
            await PlannerToolRunner.AnnounceAsync(call.Name, args, "Planner", string.Empty, recorder, progress, cancellationToken, badge);

        return call.Name switch
        {
            PlannerTools.Investigate => await InvestigateAsync(args, input, run, tools, recorder, progress, badge, cancellationToken),
            PlannerTools.AskUser => AskUser(args, input, run),
            PlannerTools.SubmitPlan => SubmitPlan(call.Arguments, input.Scope, input.AutoTesting, recorder),
            _ => await runner.RunAsync(call.Name, args, input, tools, run.MemoriesRead, string.Empty, recorder, progress, cancellationToken)
        };
    }
}
