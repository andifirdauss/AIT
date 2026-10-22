using System.Text.Json.Nodes;
using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    // Every answer leaves with its secrets masked: file contents already are (WorkspaceTools), this covers git, symbol
    // lookups, build and test output and memories.
    public async Task<PlannerToolOutcome> RunAsync(
        string tool,
        JsonObject args,
        PlannerInput input,
        WorkspaceTools tools,
        HashSet<Guid> memoriesRead,
        string prefix,
        ThreadRecorder recorder,
        ClientProgress progress,
        CancellationToken cancellationToken)
    {
        var outcome = await RunToolAsync(tool, args, input, tools, memoriesRead, prefix, recorder, progress, cancellationToken);
        return outcome with { Output = secrets.Redact(outcome.Output) };
    }
}
