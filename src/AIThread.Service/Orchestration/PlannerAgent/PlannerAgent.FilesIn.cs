using AIThread.Service.Approvals;
using AIThread.Common.Enums;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    // How many files of the plan's tasks lie in an outside folder (shown in the approval request).
    private static int FilesIn(ThreadPlan plan, string folder) =>
        plan.Tasks.SelectMany(t => t.To is null ? [t.File] : new[] { t.File, t.To })
            .Where(f => Path.IsPathRooted(f) && ApprovalTargets.Covers(ApprovalKind.WriteOutside, folder, f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
}
