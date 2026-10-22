using AIThread.Common.Enums;
using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    // requireTest: Auto Testing is on, so a plan that changes files needs its test section. unitTests: the catalog the
    // plan's tests section is checked against (null: the section is dropped).
    private static PlannerToolOutcome SubmitPlan(string arguments, WorkspaceScope scope, bool requireTest, UnitTestRules? unitTests, ThreadRecorder recorder)
    {
        if (!ThreadPlan.TryParse(arguments, out var plan, out var parseError) || plan is null)
            return new PlannerToolOutcome($"Plan rejected: invalid JSON ({parseError}). Call submit_plan again.", null, true);

        var errors = PlanValidator.Validate(plan, scope, requireTest, unitTests);
        if (errors.Count > 0)
        {
            recorder.Log(ActivityLevel.Warning, $"Rencana ditolak validator: {string.Join("; ", errors)}");
            return new PlannerToolOutcome($"Plan rejected. Fix these problems and call submit_plan again:\n- {string.Join("\n- ", errors)}", null, true);
        }

        return new PlannerToolOutcome("Plan accepted.", plan);
    }
}
