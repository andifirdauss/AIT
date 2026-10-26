using AIThread.Common.Enums;
using AIThread.Common.Localization;
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
            var project = await projectService.GetContextAsync(request.ProjectId, cancellationToken) ?? throw new AppValidationException(Lang.T("Project tidak ditemukan.", "Project not found."));
            await progress.AnchorAsync(ThreadAnchor.Line(project.Name, threadId), cancellationToken);
            var path = await testScenarios.WriteToProjectAsync(project, scenario.Id, cancellationToken);
            var todo = scenario.Missing.Count == 0 ? string.Empty : Lang.T("\n\nYang masih perlu diisi:\n", "\n\nStill to fill in:\n") + string.Concat(scenario.Missing.Select(m => $"- {m}\n"));
            await progress.FinalAsync(
                Lang.T($"📄 **Skenario test dibuat:** `{path}` ({scenario.Steps.Count} langkah, {scenario.Roles.Count} peran", $"📄 **Test scenario created:** `{path}` ({scenario.Steps.Count} steps, {scenario.Roles.Count} roles") +
                (scenario.Fields.Count == 0 ? ")" : Lang.T($", kolom form: {string.Join(", ", scenario.Fields)})", $", form fields: {string.Join(", ", scenario.Fields)})")) + "\n\n" +
                Lang.T("Buka file itu, isi sel kuning (sheet **Akun**: username tiap peran; sheet **Langkah**: nilai form), boleh menambah atau mengubah langkah, simpan, ", "Open that file, fill in the yellow cells (sheet **Accounts**: the username of each role; sheet **Steps**: form values), you may add or change steps, save, ") +
                Lang.T($"lalu balas `jalankan skenario {scenario.Slug}`. Username yang belum ada otomatis ditambahkan sebagai akun uji project.", $"then reply `run scenario {scenario.Slug}`. Usernames that do not exist yet are added as test accounts of the project automatically.") + todo, cancellationToken);
            activityLog.Info("thread", Lang.T($"Skenario {scenario.Slug} ditulis ke {path}.", $"Scenario {scenario.Slug} written to {path}."), threadId, request.ProjectId);
        }
        catch (AppValidationException ex)
        {
            await progress.FinalAsync(Lang.T($"📄 Skenario test lanjutan {number} tidak bisa dibuat: {ex.Message}", $"📄 Test scenario of follow-up test {number} cannot be created: {ex.Message}"), cancellationToken);
        }
        return new ThreadResult(threadId, ThreadStatus.Completed, progress.Transcript, 0);
    }
}
