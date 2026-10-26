using System.Diagnostics;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using AIThread.Service.Testing;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // The developer answered a tester's login question in the chat: the session keeps the login for its later threads,
    // and the thread's test runs again with it, streamed back like a thread (anchor, progress, the test, a footer).
    private async Task<ThreadResult> TestWithLoginAsync(Guid threadId, TestLogin login, ThreadRequest request, IThreadObserver observer, CancellationToken cancellationToken)
    {
        testLogins.Remember(request.SessionId!, request.ProjectId, login);
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
            await progress.FinalAsync(Lang.T($"🔐 Login diterima untuk sesi ini, tetapi testing tidak bisa diulang: {ex.Message}", $"🔐 Login accepted for this session, but testing cannot be repeated: {ex.Message}"), cancellationToken);
            return new ThreadResult(threadId, ThreadStatus.Completed, progress.Transcript, 0);
        }

        await progress.AnchorAsync(ThreadAnchor.Line(prepared.Project.Name, threadId), cancellationToken);
        activityLog.Info("thread", Lang.T($"Login untuk testing diterima dari sesi (user {login.Username}); testing diulang.", $"Login for testing received from the session (user {login.Username}); testing again."), threadId, prepared.Project.ProjectId);
        var result = await RetestAsync(prepared, login, progress, canAskLogin: true);

        await progress.FinalAsync(result is null
            ? Lang.T("🟡 Testing ulang berhenti sebelum selesai (lihat aktivitas di detail thread).", "🟡 Testing again stopped before it finished (see the activity in the thread detail).")
            : FinalReport.Testing(result.Report).TrimStart('\n'), cancellationToken);
        await progress.FinalAsync(FinalReport.Footer(total.Elapsed, result?.Tokens ?? 0, 0, null, false, null, request.DashboardUrl, threadId), cancellationToken);
        return new ThreadResult(threadId, ThreadStatus.Completed, progress.Transcript, result?.Tokens ?? 0);
    }
}
