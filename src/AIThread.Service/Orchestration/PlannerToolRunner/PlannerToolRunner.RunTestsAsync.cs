using System.Text.Json.Nodes;
using AIThread.Common.Enums;
using AIThread.Service.Memory;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    // The tool line is written by AnnounceAsync; the result goes under it.
    private async Task<PlannerToolOutcome> RunTestsAsync(PlannerInput input, JsonObject args, string prefix, ThreadRecorder recorder, ClientProgress progress,
        CancellationToken cancellationToken)
    {
        if (input.UnitTests is not { } session)
            return new PlannerToolOutcome("Error: this project has no unit test catalog AIT can run.", null, true);

        var (output, headline) = await unitTests.RunForPlannerAsync(session, MemoryAi.Strings(args["tests"]), Str(args, "file"), Bool(args, "all"), cancellationToken);
        await progress.DetailAsync(headline, cancellationToken, hang: Hang(prefix));
        var failed = output.StartsWith("Error:", StringComparison.Ordinal);
        recorder.Log(failed ? ActivityLevel.Warning : ActivityLevel.Info, $"Planner → run_tests: {headline}");
        return new PlannerToolOutcome(output, null, failed);
    }
}
