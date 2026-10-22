using System.Diagnostics;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // The developer answered a tester's dev_password question in the chat: the value becomes the project's development
    // password (shared with the team; only a development bypass belongs there) and the thread's test runs again with the
    // test accounts, streamed back like a thread.
    private async Task<ThreadResult> TestWithDevPasswordAsync(Guid threadId, string devPassword, ThreadRequest request, IThreadObserver observer,
        CancellationToken cancellationToken)
    {
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        var progress = new ClientProgress(observer, settings.StreamProgressToClient);
        var total = Stopwatch.StartNew();

        PreparedRetest prepared;
        try
        {
            prepared = await PrepareRetestAsync(threadId, cancellationToken, cancellationToken);
            if (string.IsNullOrEmpty(prepared.Project.Key)) throw new AppValidationException("project ini belum punya project key, jadi password dev tidak bisa disimpan.");
            await testAccounts.SaveDevPasswordAsync(prepared.Project.Key, devPassword, TestingSettings.SourceChat, settings.EffectiveDeveloperName, cancellationToken);
        }
        catch (AppValidationException ex)
        {
            await progress.FinalAsync($"🔐 Password dev tidak dipakai: {ex.Message}", cancellationToken);
            return new ThreadResult(threadId, ThreadStatus.Completed, progress.Transcript, 0);
        }

        await progress.AnchorAsync(ThreadAnchor.Line(prepared.Project.Name, threadId), cancellationToken);
        activityLog.Info("thread", $"Password dev {prepared.Project.Name} diperbarui dari chat oleh {settings.EffectiveDeveloperName}; testing diulang.", threadId,
            prepared.Project.ProjectId);
        await progress.StepAsync($"🔐 Password dev project disimpan (oleh {settings.EffectiveDeveloperName}); testing diulang", cancellationToken, hang: 3);
        var result = await RetestAsync(prepared, null, progress, canAskLogin: true);

        await progress.FinalAsync(result is null
            ? "🟡 Testing ulang berhenti sebelum selesai (lihat aktivitas di detail thread)."
            : FinalReport.Testing(result.Report).TrimStart('\n'), cancellationToken);
        await progress.FinalAsync(FinalReport.Footer(total.Elapsed, result?.Tokens ?? 0, 0, null, false, null, request.DashboardUrl, threadId), cancellationToken);
        return new ThreadResult(threadId, ThreadStatus.Completed, progress.Transcript, result?.Tokens ?? 0);
    }
}
