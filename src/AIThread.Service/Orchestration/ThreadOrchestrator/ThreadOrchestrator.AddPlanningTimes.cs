using AIThread.Common.Entities;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // Added to what the thread has: a planner paused by a question reports each part of its run separately.
    private static void AddPlanningTimes(ThreadRun thread, PlanningResult planning)
    {
        thread.PlanningModelMs = (thread.PlanningModelMs ?? 0) + (long)planning.ModelTime.TotalMilliseconds;
        thread.PlanningToolMs = (thread.PlanningToolMs ?? 0) + (long)planning.ToolTime.TotalMilliseconds;
        thread.PlanningQueueMs = (thread.PlanningQueueMs ?? 0) + (long)planning.QueueTime.TotalMilliseconds;
    }
}
