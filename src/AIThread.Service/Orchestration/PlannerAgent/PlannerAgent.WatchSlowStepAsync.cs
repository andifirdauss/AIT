using System.Diagnostics;
using AIThread.Common.Enums;
using AIThread.Common.Localization;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    // Runs while one planner turn waits on its model (queue included) and logs every SlowStepNotice that it still
    // waits, until the token is cancelled when the answer arrives.
    private static async Task WatchSlowStepAsync(ThreadRecorder recorder, int step, string model, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            while (true)
            {
                await Task.Delay(SlowStepNotice, cancellationToken);
                var waited = FinalReport.Duration(clock.Elapsed);
                recorder.Log(ActivityLevel.Info, Lang.T(
                    $"Planner langkah {step} masih menunggu jawaban `{model}` ({waited}; batas Timeout {timeoutSeconds} detik per panggilan).",
                    $"Planner step {step} is still waiting for `{model}` to answer ({waited}; Timeout limit {timeoutSeconds} seconds per call)."));
            }
        }
        catch (OperationCanceledException)
        {
            // The answer arrived, or the thread stopped.
        }
    }
}
