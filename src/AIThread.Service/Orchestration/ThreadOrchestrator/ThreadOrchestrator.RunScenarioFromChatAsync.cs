using System.Diagnostics;
using AIThread.Common.Enums;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // "jalankan skenario <slug>": the project's sheet .aithread/testing/<slug>.xlsx is read first (the developer just filled
    // it in), then the scenario runs on the thread it came from, streamed back like a thread. Null when the project has
    // no scenario of that name (the message is then an ordinary prompt).
    private async Task<ThreadResult?> RunScenarioFromChatAsync(string slug, bool agreed, ThreadRequest request, IThreadObserver observer, CancellationToken cancellationToken)
    {
        var project = await projectService.GetContextAsync(request.ProjectId, cancellationToken);
        if (project is null || string.IsNullOrEmpty(project.Key)) return null;
        var scenario = await testScenarios.FindAsync(project.Key, slug, cancellationToken);
        if (scenario is null) return null;

        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        var progress = new ClientProgress(observer, settings.StreamProgressToClient);
        var total = Stopwatch.StartNew();
        var threadId = scenario.SourceThreadId ?? Guid.Empty;
        try
        {
            if (await testScenarios.ImportFromProjectAsync(project, scenario, cancellationToken) is { } imported)
            {
                await progress.StepAsync($"📄 File skenario dibaca: versi {imported.Scenario.Version}, {imported.Scenario.Steps.Count} langkah" +
                                         (imported.AccountsAdded.Count == 0 ? string.Empty : $" · akun uji baru: {string.Join(", ", imported.AccountsAdded)}"), cancellationToken, hang: 3);
                foreach (var warning in imported.Warnings) await progress.StepAsync($"🟡 {warning}", cancellationToken, hang: 3);
                scenario = await testScenarios.GetAsync(scenario.Id, cancellationToken) ?? scenario;
            }
            if (await ScenarioBlockerAsync(scenario, agreed, settings.EffectiveDeveloperName, cancellationToken) is { } blocked)
            {
                var ask = blocked == SideEffectsWarning ? $"\n\nBalas `jalankan skenario {scenario.Slug} setuju` untuk melanjutkan." : string.Empty;
                await progress.FinalAsync($"🧩 **Skenario {scenario.Slug} belum dijalankan.** {blocked}{ask}", cancellationToken);
                return new ThreadResult(threadId, ThreadStatus.Completed, progress.Transcript, 0);
            }

            var prepared = await PrepareRetestAsync(threadId, cancellationToken, cancellationToken);
            await progress.AnchorAsync(ThreadAnchor.Line(prepared.Project.Name, threadId), cancellationToken);
            activityLog.Info("thread", $"Skenario {scenario.Slug} dijalankan dari chat.", threadId, project.ProjectId);
            var result = await RunScenarioAsync(prepared, scenario, progress, canAskLogin: !string.IsNullOrEmpty(request.SessionId));
            await progress.FinalAsync(result is null
                ? "🟡 Skenario berhenti sebelum selesai (lihat aktivitas di detail thread)."
                : FinalReport.Testing(result.Report).TrimStart('\n'), cancellationToken);
            await progress.FinalAsync(FinalReport.Footer(total.Elapsed, result?.Tokens ?? 0, 0, null, false, null, request.DashboardUrl, threadId), cancellationToken);
            return new ThreadResult(threadId, ThreadStatus.Completed, progress.Transcript, result?.Tokens ?? 0);
        }
        catch (AppValidationException ex)
        {
            await progress.FinalAsync($"🧩 **Skenario {scenario.Slug} belum dijalankan.** {ex.Message}", cancellationToken);
            return new ThreadResult(threadId, ThreadStatus.Completed, progress.Transcript, 0);
        }
    }
}
