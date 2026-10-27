using System.Text.Json.Nodes;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    // process_output: the latest lines of one of this thread's background processes.
    private PlannerToolOutcome ReadProcess(PlannerInput input, JsonObject args, ThreadRecorder recorder)
    {
        if (Str(args, "id") is not { } id) return new PlannerToolOutcome("Error: \"id\" is required.", null, true);
        var tail = Math.Clamp(Int(args, "tail_lines") ?? DefaultTailLines, 1, MaxTailLines);
        return processes.Output(recorder.Thread.Id, id, tail) is { } output
            ? new PlannerToolOutcome(ProcessText(output, input, stopped: false), null)
            : new PlannerToolOutcome($"Error: no background process '{id}' in this thread (start_process returns the id).", null, true);
    }
}
