using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Verification;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    // Two progress lines: the command as soon as it is known, the result under it when the build is done.
    private async Task<PlannerToolOutcome> RunBuildAsync(PlannerInput input, string? target, string prefix, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        var head = prefix + ClientProgress.Pad(PlannerTools.RunBuild, PlannerTools.NameColumn);
        var shownTarget = target is null ? string.Empty : ClientProgress.CompactPath(target, ProgressPathChars);
        var hang = Hang(prefix);
        if (!input.Settings.BuildEnabled)
        {
            await progress.StepAsync(head + shownTarget, cancellationToken, hang: hang);
            await progress.DetailAsync(Lang.T("🟡 build dimatikan di pengaturan", "🟡 build turned off in the settings"), cancellationToken, hang);
            return new PlannerToolOutcome("Error: building is disabled in the AIT settings.", null, true);
        }

        var command = BuildCommandResolver.Resolve(input.Project.RootPath, input.Project.Modules, target, out var error);
        if (command is null)
        {
            await progress.StepAsync(head + shownTarget, cancellationToken, hang: hang);
            await progress.DetailAsync($"❌ {error}", cancellationToken, hang);
            return new PlannerToolOutcome($"Error: {error}", null, true);
        }

        await progress.StepAsync(head + command.Display, cancellationToken, hang: hang);
        var result = await buildRunner.RunAsync(command, TimeSpan.FromSeconds(input.Settings.BuildTimeoutSeconds), cancellationToken);
        recorder.Log(result.Passed ? ActivityLevel.Info : ActivityLevel.Warning, $"Planner → run_build `{command.Display}`: {BuildReport.Headline(result)}");
        await progress.DetailAsync($"{(result.Passed ? "✅" : "❌")} {BuildReport.Headline(result)}", cancellationToken, hang);
        return new PlannerToolOutcome(BuildReport.Format(result, input.Scope), null);
    }
}
