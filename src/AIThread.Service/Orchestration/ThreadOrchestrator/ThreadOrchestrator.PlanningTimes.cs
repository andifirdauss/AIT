using AIThread.Common.Localization;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // "model 30.1s · tool 12.0s": where the planning time went. The wait for a planner slot shows only when there was one.
    private static string PlanningTimes(PlanningResult planning) =>
        $"model {FinalReport.Duration(planning.ModelTime)} · tool {FinalReport.Duration(planning.ToolTime)}"
        + (planning.QueueTime.TotalMilliseconds >= 100 ? Lang.T($" · antre {FinalReport.Duration(planning.QueueTime)}", $" · queued {FinalReport.Duration(planning.QueueTime)}") : string.Empty);
}
