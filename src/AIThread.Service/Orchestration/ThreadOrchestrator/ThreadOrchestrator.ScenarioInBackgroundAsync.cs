using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // Runs a scenario from the web UI; progress arrives over SSE on the thread it came from. Returns that thread's id.
    public async Task<Guid> ScenarioInBackgroundAsync(Guid projectId, Guid scenarioId, bool acknowledge, CancellationToken cancellationToken)
    {
        var project = await projectService.GetContextAsync(projectId, cancellationToken) ?? throw new AppValidationException(Lang.T("Project tidak ditemukan.", "Project not found."));
        var scenario = await testScenarios.GetAsync(scenarioId, cancellationToken);
        if (scenario is null || scenario.ProjectKey != project.Key) throw new AppValidationException(Lang.T("Skenario tidak ditemukan.", "Scenario not found."));
        var by = (await settingService.GetRuntimeAsync(cancellationToken)).EffectiveDeveloperName;
        if (await ScenarioBlockerAsync(scenario, acknowledge, by, cancellationToken) is { } blocked) throw new AppValidationException(blocked);

        var prepared = await PrepareRetestAsync(scenario.SourceThreadId!.Value, lifetime.ApplicationStopping, cancellationToken);
        _ = Task.Run(() => RunScenarioAsync(prepared, scenario, new ClientProgress(NullThreadObserver.Instance, showProgress: false), canAskLogin: false),
            CancellationToken.None);
        return prepared.Thread.Id;
    }
}
