using System.Text.Json.Nodes;
using AiThread.Service.Orchestration;
using AiThread.Service.Workspace;

namespace AiThread.Service.Interfaces;

public interface IPlannerToolRunner
{
    Task<PlannerToolOutcome> RunAsync(string tool, JsonObject args, PlannerInput input, WorkspaceTools tools, HashSet<Guid> memoriesRead, string prefix, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken);
}
