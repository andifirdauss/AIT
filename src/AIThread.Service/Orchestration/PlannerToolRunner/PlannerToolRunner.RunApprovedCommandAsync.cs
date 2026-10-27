using System.Text.Json.Nodes;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Approvals;
using AIThread.Service.Workspace.Commands;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    // The developer allowed the command (once or always): run it now; its output is the result of the paused call.
    // A start_process starts in the background instead.
    public async Task<string> RunApprovedCommandAsync(PendingCommand command, PlannerInput input, ThreadRecorder recorder, ClientProgress progress,
        CancellationToken cancellationToken)
    {
        if (command.Background) return await RunApprovedProcessAsync(command, input, recorder, progress, cancellationToken);
        await AnnounceAsync(PlannerTools.RunCommand, new JsonObject { ["command"] = secrets.Redact(command.Command) }, "Planner", string.Empty, recorder, progress, cancellationToken);
        return secrets.Redact(await ExecuteAsync(command.Command, command.WorkingDirectory, input, string.Empty, recorder, progress, cancellationToken));
    }
}
