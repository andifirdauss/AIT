using System.Globalization;
using AIThread.Common.Enums;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // Closes the execution phase: "7/8 berhasil · 24.8s (1.8x lebih cepat dari sekuensial)"; the speed-up only when
    // running in parallel actually saved time.
    private static async Task ReportSpeedAsync(ClientProgress progress, ExecutionSummary execution, CancellationToken cancellationToken)
    {
        var succeeded = execution.Results.Count(r => r.Status == ThreadTaskStatus.Succeeded);
        var speedup = execution.WallMs > 0 ? (double)execution.SumMs / execution.WallMs : 1;
        var gain = speedup >= 1.1 ? $" ({speedup.ToString("0.0", CultureInfo.InvariantCulture)}x lebih cepat dari sekuensial)" : string.Empty;
        await progress.StepAsync($"{succeeded}/{execution.Results.Count} berhasil · {Seconds(execution.WallMs)}{gain}", cancellationToken, last: true);
    }
}
