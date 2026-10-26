using System.Text.Json.Nodes;
using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    // Every answer leaves with its secrets masked: file contents already are (WorkspaceTools), this covers git, symbol
    // lookups, build and test output and memories. A path or name the model copied from masked output carries
    // placeholders: the tool gets the real text (the progress already showed the placeholders), the answer is masked again.
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
        var real = (JsonObject)args.DeepClone();
        secrets.RestoreJson(real);
        var outcome = await RunToolAsync(tool, real, input, tools, memoriesRead, prefix, recorder, progress, cancellationToken);
        return outcome with { Output = secrets.Redact(outcome.Output) };
    }
}
