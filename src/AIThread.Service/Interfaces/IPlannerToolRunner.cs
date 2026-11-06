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

    // Runs a change_database statement the developer allowed: committed only when it affects the dry test's row count.
    Task<string> RunApprovedDatabaseChangeAsync(PendingDatabaseChange change, PlannerInput input, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken);

    // Copies the touched tables of a Prod change to a local SQLite file and tries SqliteSql there (the developer chose
    // "rehearse locally first"); the result goes back to the planner, which asks again with change_database.
    Task<string> RehearseDatabaseChangeAsync(PendingDatabaseChange change, PlannerInput input, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken);
}
