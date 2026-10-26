using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    // requireTest: Auto Testing is on, so a plan that changes files needs its test section. unitTests: the catalog the
    // plan's tests section is checked against (null: the section is dropped). actors: the test accounts and their roles
    // a test scenario may name.
    private static PlannerToolOutcome SubmitPlan(string arguments, WorkspaceScope scope, bool requireTest, UnitTestRules? unitTests,
        IReadOnlyCollection<string>? actors, ThreadRecorder recorder)
    {
        if (!ThreadPlan.TryParse(arguments, out var plan, out var parseError) || plan is null)
            return new PlannerToolOutcome($"Plan rejected: invalid JSON ({parseError}). Call submit_plan again.", null, true);

        var errors = PlanValidator.Validate(plan, scope, requireTest, unitTests, actors);
        if (errors.Count > 0)
        {
            recorder.Log(ActivityLevel.Warning, Lang.T($"Rencana ditolak validator: {string.Join("; ", errors)}", $"Plan rejected by the validator: {string.Join("; ", errors)}"));
            return new PlannerToolOutcome($"Plan rejected. Fix these problems and call submit_plan again:\n- {string.Join("\n- ", errors)}", null, true);
        }

        return new PlannerToolOutcome("Plan accepted.", plan);
    }
}
