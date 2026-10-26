using AIThread.Common.DTOs;
using AIThread.Common.Localization;

namespace AIThread.Service.Implementations;

public sealed partial class SetupProfileService
{
    // The proxy API key is not part of a profile: each machine keeps its own.
    private async Task PlanOrchestrationAsync(SetupProfileOrchestration p, ImportPlan plan, CancellationToken cancellationToken)
    {
        var current = await settingService.GetAsync(string.Empty, cancellationToken);
        var passthrough = Clean(p.PassthroughModel);
        var diffs = new List<string>();
        Diff(diffs, Lang.T("Orkestrasi", "Orchestration"), OnOff(current.OrchestrationEnabled), OnOff(p.OrchestrationEnabled));
        Diff(diffs, Lang.T("Progres di CLI", "Progress in the CLI"), OnOff(current.StreamProgressToClient), OnOff(p.StreamProgressToClient));
        Diff(diffs, Lang.T("Planner boleh bertanya", "Planner may ask"), OnOff(current.PlannerCanAsk), OnOff(p.PlannerCanAsk));
        Diff(diffs, "Model passthrough", current.PassthroughModel ?? Lang.T("otomatis", "automatic"), passthrough ?? Lang.T("otomatis", "automatic"));
        Diff(diffs, Lang.T("Langkah planner", "Planner steps"), current.PlannerMaxSteps, p.PlannerMaxSteps);
        Diff(diffs, "Retry executor", current.ExecutorMaxRetries, p.ExecutorMaxRetries);
        Diff(diffs, Lang.T("Maks paralel", "Max parallel"), current.MaxParallelTasks, p.MaxParallelTasks);
        Diff(diffs, Lang.T("Timeout (detik)", "Timeout (seconds)"), current.RequestTimeoutSeconds, p.RequestTimeoutSeconds);
        Diff(diffs, Lang.T("Build & verifikasi", "Build & verification"), OnOff(current.BuildEnabled), OnOff(p.BuildEnabled));
        Diff(diffs, Lang.T("Timeout build (detik)", "Build timeout (seconds)"), current.BuildTimeoutSeconds, p.BuildTimeoutSeconds);
        Diff(diffs, Lang.T("Putaran perbaikan", "Fix rounds"), current.MaxFixRounds, p.MaxFixRounds);
        if (diffs.Count == 0) return;

        plan.Change(SetupProfileSection.Orchestration, string.Join(" · ", diffs));
        plan.Actions.Add((SetupProfileSection.Orchestration, async () =>
        {
            // Read again: the router section may have just changed the URL this request has to carry.
            var now = await settingService.GetAsync(string.Empty, cancellationToken);
            await settingService.UpdateAsync(new UpdateSystemSettingRequest(now.NineRouterBaseUrl, null, false, now.ProxyApiKey, passthrough,
                p.OrchestrationEnabled, p.StreamProgressToClient, p.PlannerMaxSteps, p.ExecutorMaxRetries, p.MaxParallelTasks,
                p.RequestTimeoutSeconds, p.BuildEnabled, p.BuildTimeoutSeconds, p.MaxFixRounds, p.PlannerCanAsk), string.Empty, cancellationToken);
        }));
    }
}
