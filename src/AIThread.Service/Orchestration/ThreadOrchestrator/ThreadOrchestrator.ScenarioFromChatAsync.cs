using AIThread.Common.Enums;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // "buat skenario test lanjutan <n>": the sheet of that suggestion, written to .aithread/testing/<slug>.xlsx in the
    // project for the developer to fill in and run from the chat.
    private async Task<ThreadResult> ScenarioFromChatAsync(Guid threadId, int number, ThreadRequest request, IThreadObserver observer, CancellationToken cancellationToken)
    {
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        var progress = new ClientProgress(observer, settings.StreamProgressToClient);
        try
        {
            var scenario = await testScenarios.CreateFromFollowUpAsync(threadId, number, cancellationToken);
            var project = await projectService.GetContextAsync(request.ProjectId, cancellationToken) ?? throw new AppValidationException("Project tidak ditemukan.");
            await progress.AnchorAsync(ThreadAnchor.Line(project.Name, threadId), cancellationToken);
            var path = await testScenarios.WriteToProjectAsync(project, scenario.Id, cancellationToken);
            var todo = scenario.Missing.Count == 0 ? string.Empty : "\n\nYang masih perlu diisi:\n" + string.Concat(scenario.Missing.Select(m => $"- {m}\n"));
            await progress.FinalAsync(
                $"📄 **Skenario test dibuat:** `{path}` ({scenario.Steps.Count} langkah, {scenario.Roles.Count} peran" +
                (scenario.Fields.Count == 0 ? ")" : $", kolom form: {string.Join(", ", scenario.Fields)})") + "\n\n" +
                "Buka file itu, isi sel kuning (sheet **Akun**: username tiap peran; sheet **Langkah**: nilai form), boleh menambah atau mengubah langkah, simpan, " +
                $"lalu balas `jalankan skenario {scenario.Slug}`. Username yang belum ada otomatis ditambahkan sebagai akun uji project." + todo, cancellationToken);
            activityLog.Info("thread", $"Skenario {scenario.Slug} ditulis ke {path}.", threadId, request.ProjectId);
        }
        catch (AppValidationException ex)
        {
            await progress.FinalAsync($"📄 Skenario test lanjutan {number} tidak bisa dibuat: {ex.Message}", cancellationToken);
        }
        return new ThreadResult(threadId, ThreadStatus.Completed, progress.Transcript, 0);
    }
}
