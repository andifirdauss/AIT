using System.Text.Json.Nodes;
using AIThread.Service.Workspace.Commands;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    // The developer allowed a start_process: it starts now; what it printed while AIT waited is the paused call's result.
    private async Task<string> RunApprovedProcessAsync(PendingCommand command, PlannerInput input, ThreadRecorder recorder, ClientProgress progress,
        CancellationToken cancellationToken)
    {
        var wait = command.Wait ?? new BackgroundWait(null, null, 5);
        var args = new JsonObject { ["command"] = secrets.Redact(command.Command) };
        if (wait.Url is not null) args["wait_for_url"] = wait.Url;
        await AnnounceAsync(PlannerTools.StartProcess, args, "Planner", string.Empty, recorder, progress, cancellationToken);
        return secrets.Redact(await LaunchAsync(command.Command, command.WorkingDirectory, wait, input, string.Empty, recorder, progress, cancellationToken));
    }
}
