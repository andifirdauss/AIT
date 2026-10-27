namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    // submit_plan offers its publish section (commit/push) when it is on and the project is a git repository.
    private static bool CanPublish(PlannerInput input, PlannerRun run) => input.Settings.GitPublishEnabled && run.CanGit;
}
