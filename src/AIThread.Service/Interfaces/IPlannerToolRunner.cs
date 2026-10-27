using System.Text.Json.Nodes;
using AIThread.Service.Orchestration;
using AIThread.Service.Workspace;

namespace AIThread.Service.Interfaces;

public interface IPlannerToolRunner
{
    Task<PlannerToolOutcome> RunAsync(string tool, JsonObject args, PlannerInput input, WorkspaceTools tools, HashSet<Guid> memoriesRead, string prefix, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken);

    // Runs a run_command line the developer allowed after the planner paused on it; the output (secrets masked) is the
    // result of the paused call. A paused start_process (PendingCommand.Background) starts in the background instead.
    Task<string> RunApprovedCommandAsync(PendingCommand command, PlannerInput input, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken);

    // Reads a web_fetch page the developer allowed after the planner paused on it; the text (secrets masked) is the
    // result of the paused call.
    Task<string> RunApprovedFetchAsync(PendingFetch fetch, PlannerInput input, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken);
}
