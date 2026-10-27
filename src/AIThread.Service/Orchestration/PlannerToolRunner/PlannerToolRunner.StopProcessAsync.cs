using System.Text.Json.Nodes;
using AIThread.Common.Enums;
using AIThread.Common.Localization;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    // stop_process: the process tree stops and is forgotten; the planner gets its last lines.
    private async Task<PlannerToolOutcome> StopProcessAsync(PlannerInput input, JsonObject args, string prefix, ThreadRecorder recorder, ClientProgress progress,
        CancellationToken cancellationToken)
    {
        if (Str(args, "id") is not { } id) return new PlannerToolOutcome("Error: \"id\" is required.", null, true);
        if (processes.Stop(recorder.Thread.Id, id, StopTailLines) is not { } stopped)
            return new PlannerToolOutcome($"Error: no background process '{id}' in this thread (it may be stopped already).", null, true);

        var age = $"{stopped.Age.TotalSeconds:0}s";
        recorder.Log(ActivityLevel.Info, Lang.T($"Planner → stop_process {stopped.Id}: dihentikan setelah {age}", $"Planner → stop_process {stopped.Id}: stopped after {age}"));
        await progress.DetailAsync($"⏹️ {stopped.Id} · {age}", cancellationToken, hang: Hang(prefix));
        return new PlannerToolOutcome(ProcessText(stopped, input, stopped: true), null);
    }
}
