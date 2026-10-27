using System.Text.Json.Nodes;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Approvals;
using AIThread.Service.Workspace.Commands;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    private async Task<bool> AllGrantedAsync(PlannerInput input, IReadOnlyList<ApprovalItem> items, ThreadRecorder recorder, CancellationToken cancellationToken)
    {
        foreach (var item in items)
            if (!await approvals.UseAsync(input.Project.ProjectId, item.Kind, item.Target, recorder.Thread.Id, cancellationToken))
                return false;
        return items.Count > 0;
    }
}
