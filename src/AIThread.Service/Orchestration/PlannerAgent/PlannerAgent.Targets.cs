namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    // The targets of a permission request, for the activity log and the planner.
    private static string Targets(PlannerApproval approval) => string.Join(", ", approval.Request.Items.Select(i => i.Target));
}
