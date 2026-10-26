using System.Diagnostics;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // "jalankan test lanjutan <n>" in the chat: that suggestion of the thread runs, streamed back like a thread (anchor,
    // progress, the test with the suggestions that can run next, a footer).
    private async Task<ThreadResult> FollowUpFromChatAsync(Guid threadId, int number, ThreadRequest request, IThreadObserver observer, CancellationToken cancellationToken)
    {
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        var progress = new ClientProgress(observer, settings.StreamProgressToClient);
        var total = Stopwatch.StartNew();

        PreparedRetest prepared;
        try
        {
            prepared = await PrepareRetestAsync(threadId, cancellationToken, cancellationToken);
        }
        catch (AppValidationException ex)
        {
            await progress.FinalAsync(Lang.T($"🧭 Test lanjutan {number} tidak bisa dijalankan: {ex.Message}", $"🧭 Follow-up test {number} cannot run: {ex.Message}"), cancellationToken);
            return new ThreadResult(threadId, ThreadStatus.Completed, progress.Transcript, 0);
        }

        await progress.AnchorAsync(ThreadAnchor.Line(prepared.Project.Name, threadId), cancellationToken);
        activityLog.Info("thread", Lang.T($"Test lanjutan {number} diminta dari chat.", $"Follow-up test {number} requested from the chat."), threadId, prepared.Project.ProjectId);
        TestingResult? result;
        try
        {
            result = await RunFollowUpAsync(prepared, number, progress, canAskLogin: !string.IsNullOrEmpty(request.SessionId));
        }
        catch (AppValidationException ex)
        {
            await progress.FinalAsync($"🧭 {ex.Message}", cancellationToken);
            return new ThreadResult(threadId, ThreadStatus.Completed, progress.Transcript, 0);
        }

        await progress.FinalAsync(result is null
            ? Lang.T("🟡 Test lanjutan berhenti sebelum selesai (lihat aktivitas di detail thread).", "🟡 The follow-up test stopped before it finished (see the activity in the thread detail).")
            : FinalReport.Testing(result.Report).TrimStart('\n'), cancellationToken);
        await progress.FinalAsync(FinalReport.Footer(total.Elapsed, result?.Tokens ?? 0, 0, null, false, null, request.DashboardUrl, threadId), cancellationToken);
        return new ThreadResult(threadId, ThreadStatus.Completed, progress.Transcript, result?.Tokens ?? 0);
    }
}
