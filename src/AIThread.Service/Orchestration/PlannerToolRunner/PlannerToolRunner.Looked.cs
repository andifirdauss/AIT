using System.Text.Json.Nodes;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    // A lookup that answered goes into the thread's investigation log, with the start of its result unless keepResult is off.
    // The log is stored with the thread, so it gets the masked output.
    private PlannerToolOutcome Looked(string tool, JsonObject args, ThreadRecorder recorder, string output, bool keepResult = true)
    {
        if (!output.StartsWith("Error:", StringComparison.Ordinal))
            recorder.Investigation.Lookup(tool, Describe(tool, args), keepResult ? secrets.Redact(output) : null);
        return new PlannerToolOutcome(output, null);
    }
}
