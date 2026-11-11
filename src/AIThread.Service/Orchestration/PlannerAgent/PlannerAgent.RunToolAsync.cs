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

        // A delegated tool is not in the planner's list unless it reads directly; a model that calls it anyway, or has
        // used up its reading budget, is pointed to investigate. The list itself stays the same (see RunAsync).
        // run_command is shared, not handed over: only the planner can ask the developer's permission, so it is neither
        // refused here nor counted as reading.
        var holder = call.Name == PlannerTools.RunCommand ? null : run.Groups.FirstOrDefault(g => g.Tools.Contains(call.Name));
        if (holder is not null && !run.DirectRead)
            return new PlannerToolOutcome($"Error: {call.Name} is delegated to a sub agent{(run.Groups.Count > 1 ? $" (\"{holder.Key}\")" : string.Empty)}. Call investigate with your question instead.", null, true);
        if (holder is not null && Interlocked.Read(ref run.DirectReadChars) >= run.DirectReadLimit)
            return new PlannerToolOutcome($"Error: your direct reading budget is used up ({run.DirectReadLimit} characters). Call investigate with your question instead.", null, true);

        // investigate numbers its own line, commit_tasks writes its own; submit_plan has none.
        if (call.Name is not (PlannerTools.SubmitPlan or PlannerTools.Investigate or PlannerTools.CommitTasks))
            await PlannerToolRunner.AnnounceAsync(call.Name, args, "Planner", string.Empty, recorder, progress, cancellationToken, badge);

        var outcome = call.Name switch
        {
            PlannerTools.Investigate => await InvestigateAsync(args, input, run, tools, recorder, progress, badge, cancellationToken),
            PlannerTools.AskUser => AskUser(args, input, run),
            PlannerTools.SubmitPlan => await SubmitPlanAsync(call.Arguments, input, CanPublish(input, run), recorder, cancellationToken),
            PlannerTools.CommitTasks => await CommitTasksAsync(call.Arguments, input, recorder, progress, cancellationToken),
            _ => await runner.RunAsync(call.Name, args, input, tools, run.MemoriesRead, string.Empty, recorder, progress, cancellationToken)
        };
        if (holder is not null) Interlocked.Add(ref run.DirectReadChars, outcome.Output.Length);
        return outcome;
    }
}
