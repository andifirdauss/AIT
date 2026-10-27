using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Approvals;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    // A valid plan is accepted, unless it changes files outside the project root in folders the developer has not
    // allowed for good: then the thread pauses with an approval request holding the plan (ResumeApprovalAsync).
    // canPublish: commit/push is on and the project is a git repository; otherwise a publish section is dropped.
    private async Task<PlannerToolOutcome> SubmitPlanAsync(string arguments, PlannerInput input, bool canPublish, ThreadRecorder recorder, CancellationToken cancellationToken)
    {
        if (!ThreadPlan.TryParse(arguments, out var plan, out var parseError) || plan is null)
            return new PlannerToolOutcome($"Plan rejected: invalid JSON ({parseError}). Call submit_plan again.", null, true);

        plan.RestorePaths(path => secrets.Restore(path).Text);
        if (!canPublish) plan.Publish = null;

        var errors = PlanValidator.Validate(plan, input.Scope, input.AutoTesting, input.UnitTests?.Rules, Actors(input));
        if (errors.Count > 0)
        {
            recorder.Log(ActivityLevel.Warning, Lang.T($"Rencana ditolak validator: {string.Join("; ", errors)}", $"Plan rejected by the validator: {string.Join("; ", errors)}"));
            return new PlannerToolOutcome($"Plan rejected. Fix these problems and call submit_plan again:\n- {string.Join("\n- ", errors)}", null, true);
        }

        if (plan.OutsideFolders is not { Count: > 0 } folders) return new PlannerToolOutcome("Plan accepted.", plan);

        var missing = new List<string>();
        foreach (var folder in folders)
            if (!await approvals.UseAsync(input.Project.ProjectId, ApprovalKind.WriteOutside, folder, recorder.Thread.Id, cancellationToken))
                missing.Add(folder);
        if (missing.Count == 0) return new PlannerToolOutcome("Plan accepted.", plan);

        if (!input.CanApprove)
            return new PlannerToolOutcome($"Plan rejected: changing files outside the project root ({string.Join(", ", missing)}) needs the developer's permission, and they cannot be asked from here. Plan within the project root, or put the outside change in \"answer\" for the developer to make.", null, true);

        var items = missing.Select(folder => new ApprovalItem(ApprovalKind.WriteOutside, ApprovalTargets.Normalize(ApprovalKind.WriteOutside, folder),
            Lang.T($"{FilesIn(plan, folder)} file", $"{FilesIn(plan, folder)} file(s)"))).ToList();
        var text = Lang.T($"Rencana ini mengubah file di luar folder project: {plan.Summary}", $"This plan changes files outside the project folder: {plan.Summary}");
        recorder.Log(ActivityLevel.Info, Lang.T($"Planner minta izin mengubah file di luar project: {string.Join(", ", missing)}", $"The planner asks permission to change files outside the project: {string.Join(", ", missing)}"));
        return new PlannerToolOutcome(string.Empty, null, Question: PlannerQuestion.ForApproval(new PlannerApproval(new ApprovalRequest(items, text), Plan: plan)));
    }
}
