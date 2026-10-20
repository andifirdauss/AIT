using AiThread.Common.DTOs;

namespace AiThread.Service.Implementations;

public sealed partial class SetupProfileService
{
    // The proxy API key is not part of a profile: each machine keeps its own.
    private async Task PlanOrchestrationAsync(SetupProfileOrchestration p, ImportPlan plan, CancellationToken cancellationToken)
    {
        var current = await settingService.GetAsync(string.Empty, cancellationToken);
        var passthrough = Clean(p.PassthroughModel);
        var diffs = new List<string>();
        Diff(diffs, "Orkestrasi", OnOff(current.OrchestrationEnabled), OnOff(p.OrchestrationEnabled));
        Diff(diffs, "Progres di CLI", OnOff(current.StreamProgressToClient), OnOff(p.StreamProgressToClient));
        Diff(diffs, "Planner boleh bertanya", OnOff(current.PlannerCanAsk), OnOff(p.PlannerCanAsk));
        Diff(diffs, "Model passthrough", current.PassthroughModel ?? "otomatis", passthrough ?? "otomatis");
        Diff(diffs, "Langkah planner", current.PlannerMaxSteps, p.PlannerMaxSteps);
        Diff(diffs, "Retry executor", current.ExecutorMaxRetries, p.ExecutorMaxRetries);
        Diff(diffs, "Maks paralel", current.MaxParallelTasks, p.MaxParallelTasks);
        Diff(diffs, "Timeout (detik)", current.RequestTimeoutSeconds, p.RequestTimeoutSeconds);
        Diff(diffs, "Build & verifikasi", OnOff(current.BuildEnabled), OnOff(p.BuildEnabled));
        Diff(diffs, "Timeout build (detik)", current.BuildTimeoutSeconds, p.BuildTimeoutSeconds);
        Diff(diffs, "Putaran perbaikan", current.MaxFixRounds, p.MaxFixRounds);
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
