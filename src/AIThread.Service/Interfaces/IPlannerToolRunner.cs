using System.Text.Json.Nodes;
using AIThread.Service.Orchestration;
using AIThread.Service.Workspace;

namespace AIThread.Service.Interfaces;

public interface IPlannerToolRunner
{
    Task<PlannerToolOutcome> RunAsync(string tool, JsonObject args, PlannerInput input, WorkspaceTools tools, HashSet<Guid> memoriesRead, string prefix, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken);
}
